using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Translation;
using ErrorOr;
using FluentValidation;
using MediatR;

namespace BookStore.Application.Features.Translation.Commands.DeleteTranslationJob;

public record DeleteTranslationJobCommand(Guid Id) : IRequest<ErrorOr<Deleted>>;

public class DeleteTranslationJobCommandValidator : AbstractValidator<DeleteTranslationJobCommand>
{
    public DeleteTranslationJobCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Translation job id is required.");
    }
}

public class DeleteTranslationJobCommandHandler : IRequestHandler<DeleteTranslationJobCommand, ErrorOr<Deleted>>
{
    private readonly ITranslationJobRepository _jobRepository;
    private readonly ITranslationWorkspace _workspace;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteTranslationJobCommandHandler(
        ITranslationJobRepository jobRepository,
        ITranslationWorkspace workspace,
        IUnitOfWork unitOfWork)
    {
        _jobRepository = jobRepository;
        _workspace = workspace;
        _unitOfWork = unitOfWork;
    }

    public async Task<ErrorOr<Deleted>> Handle(DeleteTranslationJobCommand command, CancellationToken cancellationToken)
    {
        var job = await _jobRepository.GetByIdAsync(command.Id, cancellationToken);
        if (job is null)
        {
            return TranslationJobErrors.NotFound(command.Id);
        }

        // The worker may still be writing into the folder of an active job.
        if (job.IsActive)
        {
            return TranslationJobErrors.StillActive;
        }

        _jobRepository.Delete(job);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _workspace.Delete(job.Id);

        return Result.Deleted;
    }
}
