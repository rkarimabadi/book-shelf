using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Books;
using ErrorOr;
using FluentValidation;
using MediatR;

namespace BookStore.Application.Features.Ratings.Commands.SaveBookRating;

public record SaveBookRatingCommand(
    Guid UserId,
    Guid BookId,
    int Rating) : IRequest<ErrorOr<Success>>;

public class SaveBookRatingCommandValidator : AbstractValidator<SaveBookRatingCommand>
{
    public SaveBookRatingCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User id is required.");

        RuleFor(x => x.BookId)
            .NotEmpty().WithMessage("Book id is required.");

        RuleFor(x => x.Rating)
            .InclusiveBetween(TranslationRating.Min, TranslationRating.Max)
            .WithMessage($"Rating must be between {TranslationRating.Min} and {TranslationRating.Max}.");
    }
}

public class SaveBookRatingCommandHandler : IRequestHandler<SaveBookRatingCommand, ErrorOr<Success>>
{
    private readonly ITranslationRatingRepository _ratingRepository;
    private readonly IBookRepository _bookRepository;
    private readonly IUnitOfWork _unitOfWork;

    public SaveBookRatingCommandHandler(
        ITranslationRatingRepository ratingRepository,
        IBookRepository bookRepository,
        IUnitOfWork unitOfWork)
    {
        _ratingRepository = ratingRepository;
        _bookRepository = bookRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<ErrorOr<Success>> Handle(SaveBookRatingCommand command, CancellationToken cancellationToken)
    {
        var existing = await _ratingRepository.GetByUserAndBookAsync(
            command.UserId, command.BookId, cancellationToken);

        if (existing is not null)
        {
            var updateResult = existing.Set(command.Rating);
            if (updateResult.IsError)
            {
                return updateResult.Errors;
            }

            _ratingRepository.Update(existing);
        }
        else
        {
            if (!await _bookRepository.ExistsAsync(command.BookId, cancellationToken))
            {
                return BookErrors.Validation.NotFound(command.BookId);
            }

            var createResult = TranslationRating.Create(command.UserId, command.BookId, command.Rating);
            if (createResult.IsError)
            {
                return createResult.Errors;
            }

            _ratingRepository.Add(createResult.Value);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}
