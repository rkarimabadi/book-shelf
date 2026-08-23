using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BookStore.Application.Common.Security;
using BookStore.Application.Features.Ratings.Commands.SaveBookRating;
using BookStore.Application.Features.Ratings.Queries.GetBookRating;
using BookStore.Contracts.Books;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

[Route("api/books/{bookId:guid}/rating")]
public sealed class BookRatingsController : ApiController
{
    private readonly ISender _sender;

    public BookRatingsController(ISender sender)
    {
        _sender = sender;
    }

    // Public summary (average + count); the requesting user's own rating is null when anonymous or unrated.
    [HttpGet]
    public async Task<IActionResult> Get(Guid bookId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetBookRatingQuery(GetUserId(), bookId), cancellationToken);

        return result.Match(
            rating => Ok(new MyBookRatingResponse(rating.Average, rating.Count, rating.MyRating)),
            errors => Problem(errors));
    }

    // Save/overwrite the authenticated user's 1–5 star rating.
    [HttpPut]
    [Authorize(Policy = Policies.RequireUserRole)]
    public async Task<IActionResult> Save(Guid bookId, SaveBookRatingRequest request, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "User id claim is missing.", detail: "User.ClaimMissing");
        }

        var result = await _sender.Send(new SaveBookRatingCommand(userId.Value, bookId, request.Rating), cancellationToken);

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
