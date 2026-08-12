using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BookStore.Api.Common;
using BookStore.Application.Common.Security;
using BookStore.Application.Features.Authentication.Commands.ChangePassword;
using BookStore.Application.Features.Authentication.Commands.ExternalLogin;
using BookStore.Application.Features.Authentication.Commands.ForgotPassword;
using BookStore.Application.Features.Authentication.Commands.Login;
using BookStore.Application.Features.Authentication.Commands.Logout;
using BookStore.Application.Features.Authentication.Commands.RefreshToken;
using BookStore.Application.Features.Authentication.Commands.Register;
using BookStore.Application.Features.Authentication.Commands.ResetPassword;
using BookStore.Contracts.Authentication;
using MapsterMapper;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

[Route("api/auth")]
public sealed class AuthController : ApiController
{
    private readonly ISender _sender;
    private readonly IMapper _mapper;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;
    private readonly Core.Domain.Authentication.IAuthenticationService _authenticationService;

    public AuthController(
        ISender sender,
        IMapper mapper,
        IConfiguration configuration,
        ILogger<AuthController> logger,
        Core.Domain.Authentication.IAuthenticationService authenticationService)
    {
        _sender = sender;
        _mapper = mapper;
        _configuration = configuration;
        _logger = logger;
        _authenticationService = authenticationService;
    }

    // ----- Google OAuth (optional — disabled until GoogleOAuth:ClientId/Secret are set) -----

    /// <summary>Whether Google login is configured; the UI hides its button otherwise.</summary>
    [HttpGet("google-status")]
    public IActionResult GoogleStatus()
    {
        return Ok(GoogleOAuthDefaults.IsConfigured(_configuration));
    }

    /// <summary>
    /// Starts the Google OAuth round trip: the browser is redirected to Google, which
    /// calls back into <see cref="GoogleCallback"/> with a state-protected result.
    /// </summary>
    [HttpGet("google-login")]
    public IActionResult GoogleLogin(string returnUrl = "/")
    {
        if (!GoogleOAuthDefaults.IsConfigured(_configuration))
        {
            return NotFound();
        }

        returnUrl = GoogleOAuthDefaults.SafeReturnUrl(returnUrl);

        // Google compares the redirect_uri against the exact URI registered in the Google
        // Console, so it must reflect the public scheme/host — behind a TLS-terminating
        // proxy Request.Scheme may report http (same issue PasswordReset:BaseUrl solves);
        // GoogleOAuth:BaseUrl overrides the base when needed.
        var configuredBaseUrl = _configuration["GoogleOAuth:BaseUrl"];
        var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? $"{Request.Scheme}://{Request.Host}"
            : configuredBaseUrl.TrimEnd('/');

        // ⚠️ RedirectUri must NOT be the OAuth handler's CallbackPath: the handler owns
        // that path and would intercept the post-auth redirect too, fail with "The oauth
        // state was missing or invalid." (no state on the second leg) and bounce to
        // /login?google_error=1 — which is exactly the failure we hit in production. The
        // post-auth redirect therefore goes to the separate google-finalize action, which
        // reads the external cookie and issues the app's JWT pair.
        var actionUrl = Url.Action(nameof(GoogleFinalize), new { returnUrl });
        var redirectUri = $"{baseUrl}{actionUrl}";

        _logger.LogInformation(
            "Google login: baseUrl={BaseUrl} actionUrl={ActionUrl} redirectUri={RedirectUri}",
            baseUrl, actionUrl, redirectUri);

        var properties = new AuthenticationProperties { RedirectUri = redirectUri };

        return Challenge(properties, GoogleOAuthDefaults.SchemeName);
    }

    /// <summary>
    /// The OAuth handler's callback endpoint. NOTE: every request to this path is
    /// intercepted by the Google handler itself (state/code validation, token exchange,
    /// external-cookie sign-in); the controller action below is shadowed and effectively
    /// dead while the scheme is registered. It is kept for safety when the scheme is NOT
    /// configured (defensive 404) and for reference. The real continuation of the flow is
    /// <see cref="GoogleFinalize"/>.
    /// </summary>
    [HttpGet("google-callback")]
    public IActionResult GoogleCallback()
    {
        if (!GoogleOAuthDefaults.IsConfigured(_configuration))
        {
            return NotFound();
        }

        return Redirect("/login?google_error=1");
    }

    /// <summary>
    /// Continuation of the Google round trip (NOT the OAuth CallbackPath). After the OAuth
    /// handler exchanges the code it redirects here with the external sign-in cookie
    /// (<c>BookStore.GoogleExternal</c>) set; we read that verified identity, exchange it
    /// for the app's own JWT pair and hand the tokens back to the WASM client via the
    /// /auth/callback page (which full-reloads into the authenticated shell).
    /// </summary>
    [HttpGet("google-finalize")]
    public async Task<IActionResult> GoogleFinalize(CancellationToken cancellationToken, string returnUrl = "/")
    {
        if (!GoogleOAuthDefaults.IsConfigured(_configuration))
        {
            return NotFound();
        }

        returnUrl = GoogleOAuthDefaults.SafeReturnUrl(returnUrl);

        var authResult = await HttpContext.AuthenticateAsync(GoogleOAuthDefaults.SignInScheme);
        if (!authResult.Succeeded || authResult.Principal is null)
        {
            _logger.LogWarning(
                "Google finalize: external-cookie authentication failed (Failure: {Failure}).",
                authResult.Failure?.Message ?? "no external identity");
            return Redirect("/login?google_error=1");
        }

        // The external cookie is single-use for this round trip; clear it immediately.
        await HttpContext.SignOutAsync(GoogleOAuthDefaults.SignInScheme);

        var email = authResult.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning("Google finalize: no email claim on the verified identity.");
            return Redirect("/login?google_error=1");
        }

        var givenName = authResult.Principal.FindFirstValue(ClaimTypes.GivenName) ?? string.Empty;
        var familyName = authResult.Principal.FindFirstValue(ClaimTypes.Surname) ?? string.Empty;

        var command = new ExternalLoginCommand(email, givenName, familyName);
        var result = await _sender.Send(command, cancellationToken);
        if (result.IsError)
        {
            _logger.LogWarning(
                "Google finalize: ExternalLoginCommand failed for {Email} — {Errors}.",
                email,
                string.Join(", ", result.Errors.Select(e => e.Code)));
            return Redirect("/login?google_error=1");
        }

        var session = result.Value;
        var callbackUrl = $"/auth/callback" +
            $"?access_token={Uri.EscapeDataString(session.Token)}" +
            $"&refresh_token={Uri.EscapeDataString(session.RefreshToken)}" +
            $"&returnUrl={Uri.EscapeDataString(returnUrl)}";

        return Redirect(callbackUrl);
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

        // HasPassword drives the change-password page (Google-created accounts can SET a
        // password without proving a current one). Falls back to true if the user row can't
        // be loaded — the claims are the primary contract; HasPassword is an enhancement.
        var hasPassword = true;
        if (!string.IsNullOrWhiteSpace(email))
        {
            var userResult = _authenticationService.GetUserByEmail(email);
            if (!userResult.IsError)
            {
                hasPassword = userResult.Value.HasPassword;
            }
        }

        return Ok(new
        {
            UserId = userId,
            Email = email,
            Role = role,
            HasPassword = hasPassword
        });
    }
}
