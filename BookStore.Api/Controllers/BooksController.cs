using BookStore.Application.Common.Security;
using BookStore.Application.Features.Books.Commands.CreateBook;
using BookStore.Application.Features.Books.Commands.DeleteBook;
using BookStore.Application.Features.Books.Commands.UpdateBook;
using BookStore.Application.Features.Books.Queries.GetBook;
using BookStore.Application.Features.Books.Queries.GetBooks;
using BookStore.Application.Common.Interfaces;
using BookStore.Contracts.Books;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

[Route("api/books")]
public sealed class BooksController : ApiController
{
    private readonly ISender _sender;
    private readonly IFileStorage _fileStorage;
    private readonly IWebHostEnvironment _env;

    public BooksController(ISender sender, IFileStorage fileStorage, IWebHostEnvironment env)
    {
        _sender = sender;
        _fileStorage = fileStorage;
        _env = env;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await _sender.Send(new GetBooksQuery());

        return result.Match(
            books => Ok(books.Select(book => new BookResponse(
                book.Id,
                book.Title,
                book.Author,
                book.Description,
                book.CoverImagePath,
                book.FilePath,
                book.CreatedAt,
                book.UpdatedAt))),
            errors => Problem(errors));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _sender.Send(new GetBookQuery(id));

        return result.Match(
            book => Ok(new BookResponse(
                book.Id,
                book.Title,
                book.Author,
                book.Description,
                book.CoverImagePath,
                book.FilePath,
                book.CreatedAt,
                book.UpdatedAt)),
            errors => Problem(errors));
    }

    [HttpGet("{id:guid}/download")]
    [Authorize]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetBookQuery(id), cancellationToken);
        if (result.IsError)
        {
            return Problem(result.Errors);
        }

        var book = result.Value;
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
                    book.Description,
                    book.CoverImagePath,
                    book.FilePath,
                    book.CreatedAt,
                    book.UpdatedAt)),
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
            request.Description,
            coverImagePath,
            filePath);

        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            book => Ok(new BookResponse(
                book.Id,
                book.Title,
                book.Author,
                book.Description,
                book.CoverImagePath,
                book.FilePath,
                book.CreatedAt,
                book.UpdatedAt)),
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
