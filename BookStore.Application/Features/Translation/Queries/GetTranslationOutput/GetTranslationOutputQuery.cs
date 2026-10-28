using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Translation;
using ErrorOr;
using FluentValidation;
using MediatR;

namespace BookStore.Application.Features.Translation.Queries.GetTranslationOutput;

/// <summary>Where the finished Persian EPUB lives, plus the download name to offer.</summary>
public sealed record TranslationOutput(string FullPath, string DownloadName);

public record GetTranslationOutputQuery(Guid Id) : IRequest<ErrorOr<TranslationOutput>>;

public class GetTranslationOutputQueryValidator : AbstractValidator<GetTranslationOutputQuery>
{
    public GetTranslationOutputQueryValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Translation job id is required.");
    }
}

public class GetTranslationOutputQueryHandler : IRequestHandler<GetTranslationOutputQuery, ErrorOr<TranslationOutput>>
{
    private readonly ITranslationJobRepository _jobRepository;
    private readonly ITranslationWorkspace _workspace;

    public GetTranslationOutputQueryHandler(ITranslationJobRepository jobRepository, ITranslationWorkspace workspace)
    {
        _jobRepository = jobRepository;
        _workspace = workspace;
    }

    public async Task<ErrorOr<TranslationOutput>> Handle(GetTranslationOutputQuery query, CancellationToken cancellationToken)
    {
        var job = await _jobRepository.GetByIdAsync(query.Id, cancellationToken);
        if (job is null)
        {
            return TranslationJobErrors.NotFound(query.Id);
        }

        if (job.Status != TranslationJobStatus.Completed)
        {
            return TranslationJobErrors.NotCompleted;
        }

        var path = _workspace.GetOutputPath(job.Id);
        if (path is null)
        {
            return TranslationJobErrors.OutputMissing;
        }

        var baseName = Path.GetFileNameWithoutExtension(job.OriginalFileName);
        return new TranslationOutput(path, $"{baseName}-fa.epub");
    }
}
