using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Books;
using ErrorOr;
using FluentValidation;
using MediatR;

namespace BookStore.Application.Features.Notes.Commands.SaveBookNote;

public record SaveBookNoteCommand(
    Guid UserId,
    Guid BookId,
    string Note) : IRequest<ErrorOr<Success>>;

public class SaveBookNoteCommandValidator : AbstractValidator<SaveBookNoteCommand>
{
    public SaveBookNoteCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User id is required.");

        RuleFor(x => x.BookId)
            .NotEmpty().WithMessage("Book id is required.");

        RuleFor(x => x.Note)
            .MaximumLength(BookNote.MaxLength).WithMessage($"Note must not exceed {BookNote.MaxLength} characters.");
    }
}

public class SaveBookNoteCommandHandler : IRequestHandler<SaveBookNoteCommand, ErrorOr<Success>>
{
    private readonly IBookNoteRepository _bookNoteRepository;
    private readonly IBookRepository _bookRepository;
    private readonly IUnitOfWork _unitOfWork;

    public SaveBookNoteCommandHandler(
        IBookNoteRepository bookNoteRepository,
        IBookRepository bookRepository,
        IUnitOfWork unitOfWork)
    {
        _bookNoteRepository = bookNoteRepository;
        _bookRepository = bookRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<ErrorOr<Success>> Handle(SaveBookNoteCommand command, CancellationToken cancellationToken)
    {
        var existing = await _bookNoteRepository.GetByUserAndBookAsync(
            command.UserId, command.BookId, cancellationToken);

        // An empty note means "clear it" — deleting an absent note is a harmless no-op.
        if (string.IsNullOrWhiteSpace(command.Note))
        {
            if (existing is not null)
            {
                _bookNoteRepository.Delete(existing);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result.Success;
        }

        if (existing is not null)
        {
            var updateResult = existing.Update(command.Note);
            if (updateResult.IsError)
            {
                return updateResult.Errors;
            }

            _bookNoteRepository.Update(existing);
        }
        else
        {
            if (!await _bookRepository.ExistsAsync(command.BookId, cancellationToken))
            {
                return BookErrors.Validation.NotFound(command.BookId);
            }

            var createResult = BookNote.Create(command.UserId, command.BookId, command.Note);
            if (createResult.IsError)
            {
                return createResult.Errors;
            }

            _bookNoteRepository.Add(createResult.Value);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}
