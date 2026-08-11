using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BookStore.Application.Common.Security;
using BookStore.Application.Features.Authentication.Commands.ChangePassword;
using BookStore.Application.Features.Authentication.Commands.ForgotPassword;
using BookStore.Application.Features.Authentication.Commands.Login;
using BookStore.Application.Features.Authentication.Commands.Logout;
using BookStore.Application.Features.Authentication.Commands.RefreshToken;
using BookStore.Application.Features.Authentication.Commands.Register;
using BookStore.Application.Features.Authentication.Commands.ResetPassword;
using BookStore.Contracts.Authentication;
using MapsterMapper;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

[Route("api/auth")]
public sealed class AuthController : ApiController
{
    private readonly ISender _sender;
    private readonly IMapper _mapper;
    private readonly IConfiguration _configuration;

    public AuthController(ISender sender, IMapper mapper, IConfiguration configuration)
    {
        _sender = sender;
        _mapper = mapper;
        _configuration = configuration;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var command = _mapper.Map<RegisterCommand>(request);
        var result = await _sender.Send(command);

        return result.Match(
            authResult => Ok(_mapper.Map<AuthenticationResponse>(authResult)),
            errors => Problem(errors));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var command = _mapper.Map<LoginCommand>(request);
        var result = await _sender.Send(command);

        return result.Match(
            authResult => Ok(_mapper.Map<AuthenticationResponse>(authResult)),
            errors => Problem(errors));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshTokenRequest request)
    {
        var command = _mapper.Map<RefreshTokenCommand>(request);
        var result = await _sender.Send(command);

        return result.Match(
            authResult => Ok(_mapper.Map<AuthenticationResponse>(authResult)),
            errors => Problem(errors));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(LogoutRequest request)
    {
        var command = _mapper.Map<LogoutCommand>(request);
        var result = await _sender.Send(command);

        return result.Match(
            _ => NoContent(),
            errors => Problem(errors));
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        // The WASM client and the API share one origin, so the reset link points at the
        // client route with the same host the user is currently on. An explicit
        // PasswordReset:BaseUrl overrides this (e.g. https://books.example.com) for hosts
        // that terminate TLS at a proxy/ARR where Request.Scheme may report http.
        var configuredBaseUrl = _configuration["PasswordReset:BaseUrl"];
        var resetLinkBase = string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? $"{Request.Scheme}://{Request.Host}/reset-password"
            : $"{configuredBaseUrl.TrimEnd('/')}/reset-password";

        var command = new ForgotPasswordCommand(request.Email, resetLinkBase);
        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            _ => NoContent(),
            errors => Problem(errors));
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var command = new ResetPasswordCommand(request.Email, request.Token, request.NewPassword);
        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            _ => NoContent(),
            errors => Problem(errors));
    }

    [HttpPost("change-password")]
    [Authorize(Policy = Policies.RequireUserRole)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var email = User.FindFirstValue(JwtRegisteredClaimNames.Email);
        if (string.IsNullOrWhiteSpace(email))
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Email claim is missing from the token.");
        }

        var command = new ChangePasswordCommand(email, request.CurrentPassword, request.NewPassword);
        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            _ => NoContent(),
            errors => Problem(errors));
    }

    [HttpGet("me")]
    [Authorize(Policy = Policies.RequireUserRole)]
    public IActionResult Me()
    {
        var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var email = User.FindFirstValue(JwtRegisteredClaimNames.Email);
        var role = User.FindFirstValue("role");

        return Ok(new
        {
            UserId = userId,
            Email = email,
            Role = role
        });
    }
}
