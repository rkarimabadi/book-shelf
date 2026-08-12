using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BookStore.Application.Common.Security;
using BookStore.Application.Features.Users.Commands.DeleteUser;
using BookStore.Application.Features.Users.Commands.ResetUserPassword;
using BookStore.Application.Features.Users.Commands.SetUserRole;
using BookStore.Application.Features.Users.Commands.SetUserStatus;
using BookStore.Application.Features.Users.Queries.GetUsers;
using BookStore.Contracts.Users;
using BookStore.Core.Domain.Users;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

[Route("api/users")]
[Authorize(Policy = Policies.RequireAdminRole)]
public sealed class UsersController : ApiController
{
    private readonly ISender _sender;

    public UsersController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetUsersQuery(), cancellationToken);

        return result.Match(
            users => Ok(users.Select(user => new UserResponse(
                user.Id,
                user.Email,
                user.FirstName,
                user.LastName,
                user.Role.ToString(),
                user.IsActive,
                user.CreatedAt,
                user.LastLoginAt))),
            errors => Problem(errors));
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, SetUserStatusRequest request, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "User id claim is missing from the token.");
        }

        var command = new SetUserStatusCommand(id, request.IsActive, currentUserId.Value);
        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            _ => NoContent(),
            errors => Problem(errors));
    }

    [HttpPatch("{id:guid}/role")]
    public async Task<IActionResult> SetRole(Guid id, SetUserRoleRequest request, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "User id claim is missing from the token.");
        }

        if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var role) || !Enum.IsDefined(role))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Role is invalid.", detail: "User.InvalidRole");
        }

        var command = new SetUserRoleCommand(id, role, currentUserId.Value);
        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            _ => NoContent(),
            errors => Problem(errors));
    }

    [HttpPatch("{id:guid}/password")]
    public async Task<IActionResult> ResetPassword(Guid id, ResetUserPasswordRequest request, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "User id claim is missing from the token.");
        }

        var command = new ResetUserPasswordCommand(id, request.Password, currentUserId.Value);
        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            _ => NoContent(),
            errors => Problem(errors));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "User id claim is missing from the token.");
        }

        var command = new DeleteUserCommand(id, currentUserId.Value);
        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            _ => NoContent(),
            errors => Problem(errors));
    }

    private Guid? GetCurrentUserId()
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
