using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Translation;
using ErrorOr;
using FluentValidation;
using MediatR;

namespace BookStore.Application.Features.Translation.Commands.CreateTranslationJob;

/// <param name="Epub">Seekable stream of the uploaded EPUB.</param>
/// <param name="Cover">Optional replacement cover image (seekable), with its file extension.</param>
public record CreateTranslationJobCommand(
    Guid UserId,
    string FileName,
    Stream Epub,
    Stream? Cover,
    string? CoverExtension,
    string? Title,
    string? Author,
    TranslationMode Mode,
    string? Instructions) : IRequest<ErrorOr<TranslationJob>>;

public class CreateTranslationJobCommandValidator : AbstractValidator<CreateTranslationJobCommand>
{
    public static readonly string[] CoverExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];

    public CreateTranslationJobCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User id is required.");

        RuleFor(x => x.FileName)
            .NotEmpty().WithMessage("Book file is required.")
            .Must(name => string.Equals(Path.GetExtension(name), ".epub", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Book file must be an EPUB.");

        RuleFor(x => x.CoverExtension)
            .Must(ext => CoverExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            .When(x => x.Cover is not null)
            .WithMessage("Cover image must be a JPG, PNG, WEBP or GIF file.");

        RuleFor(x => x.Title)
            .MaximumLength(TranslationJob.MaxTitleLength).WithMessage($"Title must not exceed {TranslationJob.MaxTitleLength} characters.");

        RuleFor(x => x.Author)
            .MaximumLength(TranslationJob.MaxAuthorLength).WithMessage($"Author must not exceed {TranslationJob.MaxAuthorLength} characters.");

        RuleFor(x => x.Mode)
            .IsInEnum().WithMessage("Translation mode is invalid.");

        RuleFor(x => x.Instructions)
            .MaximumLength(TranslationJob.MaxInstructionsLength).WithMessage($"Instructions must not exceed {TranslationJob.MaxInstructionsLength} characters.");
    }
}

public class CreateTranslationJobCommandHandler : IRequestHandler<CreateTranslationJobCommand, ErrorOr<TranslationJob>>
{
    private readonly ITranslationJobRepository _jobRepository;
    private readonly IAiProviderSettingsRepository _settingsRepository;
    private readonly ITranslationWorkspace _workspace;
    private readonly ITranslationQueue _queue;
    private readonly IUnitOfWork _unitOfWork;

    public CreateTranslationJobCommandHandler(
        ITranslationJobRepository jobRepository,
        IAiProviderSettingsRepository settingsRepository,
        ITranslationWorkspace workspace,
        ITranslationQueue queue,
        IUnitOfWork unitOfWork)
    {
        _jobRepository = jobRepository;
        _settingsRepository = settingsRepository;
        _workspace = workspace;
        _queue = queue;
        _unitOfWork = unitOfWork;
    }

    public async Task<ErrorOr<TranslationJob>> Handle(CreateTranslationJobCommand command, CancellationToken cancellationToken)
    {
        // Fail fast: a job queued without a provider would only fail in the worker.
        if (await _settingsRepository.GetAsync(cancellationToken) is null)
        {
            return AiProviderSettingsErrors.NotConfigured;
        }

        var info = _workspace.Inspect(command.Epub);
        if (info.IsError)
        {
            return info.Errors;
        }

        var jobResult = TranslationJob.Create(
            command.UserId,
            Path.GetFileName(command.FileName),
            info.Value.Title,
            info.Value.Words,
            command.Title,
            command.Author,
            command.Mode,
            command.Instructions,
            hasCustomCover: command.Cover is not null);

        if (jobResult.IsError)
        {
            return jobResult.Errors;
        }

        var job = jobResult.Value;

        try
        {
            command.Epub.Position = 0;
            await _workspace.SaveSourceAsync(job.Id, command.Epub, cancellationToken);
            if (command.Cover is not null)
            {
                await _workspace.SaveCoverAsync(job.Id, command.Cover, command.CoverExtension!, cancellationToken);
            }

            _jobRepository.Add(job);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            _workspace.Delete(job.Id);
            throw;
        }

        _queue.Enqueue(job.Id);

        return job;
    }
}
