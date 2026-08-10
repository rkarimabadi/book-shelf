using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Authentication;
using ErrorOr;
using FluentValidation;
using MediatR;

namespace BookStore.Application.Features.Authentication.Commands.ResetPassword;

public record ResetPasswordCommand(
    string Email,
    string Token,
    string NewPassword) : IRequest<ErrorOr<Success>>;

public class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.");

        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Reset token is required.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("New password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters long.");
    }
}

public class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand, ErrorOr<Success>>
{
    private readonly IAuthenticationService _authenticationService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IUnitOfWork _unitOfWork;

    public ResetPasswordCommandHandler(
        IAuthenticationService authenticationService,
        IPasswordHasher passwordHasher,
        IUnitOfWork unitOfWork)
    {
        _authenticationService = authenticationService;
        _passwordHasher = passwordHasher;
        _unitOfWork = unitOfWork;
    }

    public async Task<ErrorOr<Success>> Handle(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        var hashedPassword = _passwordHasher.HashPassword(command.NewPassword);

        var result = _authenticationService.ResetPassword(command.Email, command.Token, hashedPassword);
        if (result.IsError)
        {
            return result.Errors;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}
