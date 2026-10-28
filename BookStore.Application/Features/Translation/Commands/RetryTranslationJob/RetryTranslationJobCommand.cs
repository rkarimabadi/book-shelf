using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Translation;
using ErrorOr;
using FluentValidation;
using MediatR;

namespace BookStore.Application.Features.Translation.Commands.RetryTranslationJob;

/// <summary>Queues a failed/cancelled job again; chunks finished earlier are not re-sent to the AI.</summary>
public record RetryTranslationJobCommand(Guid Id) : IRequest<ErrorOr<Success>>;

public class RetryTranslationJobCommandValidator : AbstractValidator<RetryTranslationJobCommand>
{
    public RetryTranslationJobCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Translation job id is required.");
    }
}

public class RetryTranslationJobCommandHandler : IRequestHandler<RetryTranslationJobCommand, ErrorOr<Success>>
{
    private readonly ITranslationJobRepository _jobRepository;
    private readonly ITranslationQueue _queue;
    private readonly IUnitOfWork _unitOfWork;

    public RetryTranslationJobCommandHandler(
        ITranslationJobRepository jobRepository,
        ITranslationQueue queue,
        IUnitOfWork unitOfWork)
    {
        _jobRepository = jobRepository;
        _queue = queue;
        _unitOfWork = unitOfWork;
    }

    public async Task<ErrorOr<Success>> Handle(RetryTranslationJobCommand command, CancellationToken cancellationToken)
    {
        var job = await _jobRepository.GetByIdAsync(command.Id, cancellationToken);
        if (job is null)
        {
            return TranslationJobErrors.NotFound(command.Id);
        }

        var result = job.Requeue();
        if (result.IsError)
        {
            return result.Errors;
        }

        _jobRepository.Update(job);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _queue.Enqueue(job.Id);

        return Result.Success;
    }
}
