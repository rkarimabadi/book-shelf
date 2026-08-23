using BookStore.Application.Common.Security;
using BookStore.Application.Features.Books.Commands.CreateBook;
using BookStore.Application.Features.Books.Commands.DeleteBook;
using BookStore.Application.Features.Books.Commands.SetBookStatus;
using BookStore.Application.Features.Books.Commands.UpdateBook;
using BookStore.Application.Features.Books.Queries.GetBook;
using BookStore.Application.Features.Books.Queries.GetBookCategories;
using BookStore.Application.Features.Books.Queries.GetBooks;
using BookStore.Application.Common.Interfaces;
using BookStore.Contracts.Books;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;

namespace BookStore.Api.Controllers;

[Route("api/books")]
public sealed class BooksController : ApiController
{
    private readonly ISender _sender;
    private readonly IFileStorage _fileStorage;
    private readonly ICoverThumbnailGenerator _coverThumbnailGenerator;
    private readonly IWebHostEnvironment _env;

    public BooksController(ISender sender, IFileStorage fileStorage, ICoverThumbnailGenerator coverThumbnailGenerator, IWebHostEnvironment env)
    {
        _sender = sender;
        _fileStorage = fileStorage;
        _coverThumbnailGenerator = coverThumbnailGenerator;
        _env = env;
    }

    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetBookCategoriesQuery(), cancellationToken);

        return result.Match(Ok, Problem);
    }

    /// <summary>
    /// Serves a resized webp thumbnail of a cover image (generated once, cached on disk).
    /// Covers are re-uploaded under fresh filenames, so the response is immutable.
    /// </summary>
    [HttpGet("covers/{fileName}")]
    [ResponseCache(Duration = 31536000, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetCoverThumbnail(string fileName, [FromQuery] int w = 256, CancellationToken cancellationToken = default)
    {
        var width = Math.Clamp(w, 32, 1024);
        var safeName = Path.GetFileName(fileName);
        var relativePath = $"uploads/covers/{safeName}";

        try
        {
            var thumbPath = await _coverThumbnailGenerator.GetOrCreateThumbnailAsync(relativePath, width, cancellationToken);
            return PhysicalFile(Path.GetFullPath(thumbPath, _env.ContentRootPath), "image/webp");
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
        catch (UnknownImageFormatException)
        {
            return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Cover image could not be processed.");
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12,
        [FromQuery] bool includeInactive = false,
        [FromQuery] string? category = null,
        [FromQuery] string? search = null)
    {
        // Only admins may list deactivated books (the admin management page); for everyone
        // else the flag is ignored so the public catalog never leaks hidden books.
        var showInactive = includeInactive && User.IsInRole(Roles.Admin);

        var result = await _sender.Send(new GetBooksQuery(showInactive, page, pageSize, category, search));

        return result.Match(
            paged => Ok(new PagedBooksResponse(
                paged.Items.Select(book =>
                    {
                        // Books with no ratings are absent from the summaries; default to null/0.
                        paged.RatingSummaries.TryGetValue(book.Id, out var summary);

                        return new BookResponse(
                            book.Id,
                            book.Title,
                            book.Author,
                            book.Category,
                            book.Description,
                            book.CoverImagePath,
                            book.FilePath,
                            book.CreatedAt,
                            book.UpdatedAt,
                            book.IsActive,
                            summary.Average,
                            summary.Count);
                    }).ToList(),
                paged.Page,
                paged.PageSize,
                paged.TotalCount,
                paged.TotalPages)),
            errors => Problem(errors));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] bool includeInactive = false)
    {
        var showInactive = includeInactive && User.IsInRole(Roles.Admin);

        var result = await _sender.Send(new GetBookQuery(id, showInactive));

        return result.Match(
            book => Ok(new BookResponse(
                book.Book.Id,
                book.Book.Title,
                book.Book.Author,
                book.Book.Category,
                book.Book.Description,
                book.Book.CoverImagePath,
                book.Book.FilePath,
                book.Book.CreatedAt,
                book.Book.UpdatedAt,
                book.Book.IsActive,
                book.RatingAverage,
                book.RatingCount)),
            errors => Problem(errors));
    }

    [HttpGet("{id:guid}/download")]
    [Authorize(Policy = Policies.RequireUserRole)]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetBookQuery(id), cancellationToken);
        if (result.IsError)
        {
            return Problem(result.Errors);
        }

        var book = result.Value.Book;
        if (string.IsNullOrWhiteSpace(book.FilePath))
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Book file is missing.", detail: "Book.FileMissing");
        }

        var fullPath = Path.GetFullPath(_fileStorage.GetFullPath(book.FilePath), _env.ContentRootPath);
        if (!System.IO.File.Exists(fullPath))
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Book file is missing.", detail: "Book.FileMissing");
        }

        return PhysicalFile(fullPath, "application/epub+zip", Path.GetFileName(book.FilePath));
    }

    [HttpPost]
    [Authorize(Policy = Policies.RequireAdminRole)]
    public async Task<IActionResult> Create(
        [FromForm] CreateBookRequest request,
        IFormFile? coverImage,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        string coverImagePath = string.Empty;
        string filePath = string.Empty;

        if (coverImage is not null)
        {
            await using var stream = coverImage.OpenReadStream();
            coverImagePath = await _fileStorage.SaveAsync(stream, coverImage.FileName, "covers", cancellationToken);
        }

        if (file is null)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Book file is required.", detail: "Book.FileRequired");
        }

        await using var fileStream = file.OpenReadStream();
        filePath = await _fileStorage.SaveAsync(fileStream, file.FileName, "books", cancellationToken);

        var command = new CreateBookCommand(
            request.Title,
            request.Author,
            request.Category,
            request.Description,
            coverImagePath,
            filePath);

        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            book => CreatedAtAction(
                nameof(GetById),
                new { id = book.Id },
                new BookResponse(
                    book.Id,
                    book.Title,
                    book.Author,
                    book.Category,
                    book.Description,
                    book.CoverImagePath,
                    book.FilePath,
                    book.CreatedAt,
                    book.UpdatedAt,
                    book.IsActive)),
            errors => Problem(errors));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.RequireAdminRole)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromForm] UpdateBookRequest request,
        IFormFile? coverImage,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        string? coverImagePath = null;
        string? filePath = null;

        if (coverImage is not null)
        {
            await using var stream = coverImage.OpenReadStream();
            coverImagePath = await _fileStorage.SaveAsync(stream, coverImage.FileName, "covers", cancellationToken);
        }

        if (file is not null)
        {
            await using var stream = file.OpenReadStream();
            filePath = await _fileStorage.SaveAsync(stream, file.FileName, "books", cancellationToken);
        }

        var command = new UpdateBookCommand(
            id,
            request.Title,
            request.Author,
            request.Category,
            request.Description,
            coverImagePath,
            filePath);

        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            book => Ok(new BookResponse(
                book.Id,
                book.Title,
                book.Author,
                book.Category,
                book.Description,
                book.CoverImagePath,
                book.FilePath,
                book.CreatedAt,
                book.UpdatedAt,
                book.IsActive)),
            errors => Problem(errors));
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = Policies.RequireAdminRole)]
    public async Task<IActionResult> SetStatus(Guid id, UpdateBookStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new SetBookStatusCommand(id, request.IsActive), cancellationToken);

        return result.Match(
            book => Ok(new BookResponse(
                book.Id,
                book.Title,
                book.Author,
                book.Category,
                book.Description,
                book.CoverImagePath,
                book.FilePath,
                book.CreatedAt,
                book.UpdatedAt,
                book.IsActive)),
            errors => Problem(errors));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.RequireAdminRole)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new DeleteBookCommand(id), cancellationToken);

        return result.Match(
            _ => NoContent(),
            errors => Problem(errors));
    }
}
