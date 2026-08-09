using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Authentication;
using ErrorOr;
using FluentValidation;
using MediatR;

namespace BookStore.Application.Features.Authentication.Commands.Logout;

public record LogoutCommand(
    string RefreshToken) : IRequest<ErrorOr<Success>>;

public class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Refresh token is required.");
    }
}

public class LogoutCommandHandler : IRequestHandler<LogoutCommand, ErrorOr<Success>>
{
    private readonly IAuthenticationService _authenticationService;
    private readonly IUnitOfWork _unitOfWork;

    public LogoutCommandHandler(
        IAuthenticationService authenticationService,
        IUnitOfWork unitOfWork)
    {
        _authenticationService = authenticationService;
        _unitOfWork = unitOfWork;
    }

    public async Task<ErrorOr<Success>> Handle(LogoutCommand command, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var logoutResult = _authenticationService.LogoutUser(command.RefreshToken);
        if (logoutResult.IsError)
        {
            return logoutResult.Errors;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return logoutResult;
    }
}
