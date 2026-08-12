using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BookStore.Application.Common.Security;
using BookStore.Application.Features.Library.Commands.AddBookToLibrary;
using BookStore.Application.Features.Library.Commands.RemoveBookFromLibrary;
using BookStore.Application.Features.Library.Queries.GetUserLibrary;
using BookStore.Contracts.Library;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

[Route("api/library")]
[Authorize(Policy = Policies.RequireUserRole)]
public sealed class LibraryController : ApiController
{
    private readonly ISender _sender;

    public LibraryController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    public async Task<IActionResult> GetMyLibrary(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "User id claim is missing.", detail: "User.ClaimMissing");
        }

        var result = await _sender.Send(new GetUserLibraryQuery(userId.Value), cancellationToken);

        return result.Match(
            books => Ok(books.Select(entry => new LibraryBookResponse(
                entry.Book.Id,
                entry.Book.Title,
                entry.Book.Author,
                entry.Book.Category,
                entry.Book.Description,
                entry.Book.CoverImagePath,
                entry.Book.FilePath,
                entry.AddedAt))),
            errors => Problem(errors));
    }

    [HttpPost]
    public async Task<IActionResult> Add(AddBookToLibraryRequest request, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "User id claim is missing.", detail: "User.ClaimMissing");
        }

        var result = await _sender.Send(new AddBookToLibraryCommand(userId.Value, request.BookId), cancellationToken);

        return result.Match(
            _ => NoContent(),
            errors => Problem(errors));
    }

    [HttpDelete("{bookId:guid}")]
    public async Task<IActionResult> Remove(Guid bookId, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "User id claim is missing.", detail: "User.ClaimMissing");
        }

        var result = await _sender.Send(new RemoveBookFromLibraryCommand(userId.Value, bookId), cancellationToken);

        return result.Match(
            _ => NoContent(),
            errors => Problem(errors));
    }

    private Guid? GetUserId()
    {
        var userIdClaim = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (Guid.TryParse(userIdClaim, out var userId))
        {
            return userId;
        }

        return null;
    }
}
