using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Translation;
using ErrorOr;
using FluentValidation;
using MediatR;

namespace BookStore.Application.Features.Translation.Commands.CancelTranslationJob;

public record CancelTranslationJobCommand(Guid Id) : IRequest<ErrorOr<Success>>;

public class CancelTranslationJobCommandValidator : AbstractValidator<CancelTranslationJobCommand>
{
    public CancelTranslationJobCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Translation job id is required.");
    }
}

public class CancelTranslationJobCommandHandler : IRequestHandler<CancelTranslationJobCommand, ErrorOr<Success>>
{
    private readonly ITranslationJobRepository _jobRepository;
    private readonly ITranslationQueue _queue;
    private readonly IUnitOfWork _unitOfWork;

    public CancelTranslationJobCommandHandler(
        ITranslationJobRepository jobRepository,
        ITranslationQueue queue,
        IUnitOfWork unitOfWork)
    {
        _jobRepository = jobRepository;
        _queue = queue;
        _unitOfWork = unitOfWork;
    }

    public async Task<ErrorOr<Success>> Handle(CancelTranslationJobCommand command, CancellationToken cancellationToken)
    {
        var job = await _jobRepository.GetByIdAsync(command.Id, cancellationToken);
        if (job is null)
        {
            return TranslationJobErrors.NotFound(command.Id);
        }

        var result = job.Cancel();
        if (result.IsError)
        {
            return result.Errors;
        }

        _jobRepository.Update(job);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _queue.Cancel(job.Id);

        return Result.Success;
    }
}
