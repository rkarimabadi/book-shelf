using BookStore.Application.Common.Interfaces;
using BookStore.Application.Features.Authentication.Common;
using BookStore.Core.Domain.Authentication;
using ErrorOr;
using FluentValidation;
using MediatR;

namespace BookStore.Application.Features.Authentication.Commands.RefreshToken;

public record RefreshTokenCommand(
    string RefreshToken) : IRequest<ErrorOr<AuthenticationResult>>;

public class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Refresh token is required.");
    }
}

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, ErrorOr<AuthenticationResult>>
{
    private readonly IAuthenticationService _authenticationService;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IUnitOfWork _unitOfWork;

    public RefreshTokenCommandHandler(
        IAuthenticationService authenticationService,
        IJwtTokenGenerator jwtTokenGenerator,
        IUnitOfWork unitOfWork)
    {
        _authenticationService = authenticationService;
        _jwtTokenGenerator = jwtTokenGenerator;
        _unitOfWork = unitOfWork;
    }

    public async Task<ErrorOr<AuthenticationResult>> Handle(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var refreshResult = _authenticationService.RefreshToken(command.RefreshToken);
        if (refreshResult.IsError)
        {
            return refreshResult.Errors;
        }

        var (user, newRefreshToken) = refreshResult.Value;

        var token = _jwtTokenGenerator.GenerateToken(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.Role.ToString());

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthenticationResult(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.Role.ToString(),
            token,
            newRefreshToken);
    }
}
