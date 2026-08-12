using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BookStore.Application.Common.Security;
using BookStore.Application.Features.Notes.Commands.SaveBookNote;
using BookStore.Application.Features.Notes.Queries.GetBookNote;
using BookStore.Contracts.Books;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

[Route("api/books/{bookId:guid}/note")]
[Authorize(Policy = Policies.RequireUserRole)]
public sealed class BookNotesController : ApiController
{
    private readonly ISender _sender;

    public BookNotesController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    public async Task<IActionResult> Get(Guid bookId, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "User id claim is missing.", detail: "User.ClaimMissing");
        }

        var result = await _sender.Send(new GetBookNoteQuery(userId.Value, bookId), cancellationToken);

        return result.Match(
            note => Ok(new BookNoteResponse(note)),
            errors => Problem(errors));
    }

    [HttpPut]
    public async Task<IActionResult> Save(Guid bookId, SaveBookNoteRequest request, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "User id claim is missing.", detail: "User.ClaimMissing");
        }

        var result = await _sender.Send(new SaveBookNoteCommand(userId.Value, bookId, request.Note), cancellationToken);

        return result.Match(
            _ => NoContent(),
            errors => Problem(errors));
    }

    private Guid? GetUserId()
    {
        var userIdClaim = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
    }
}
