using BookStore.Core.Domain.Translation;
using ErrorOr;
using MediatR;

namespace BookStore.Application.Features.Translation.Queries.GetTranslationJobs;

public record GetTranslationJobsQuery : IRequest<ErrorOr<List<TranslationJob>>>;

public class GetTranslationJobsQueryHandler : IRequestHandler<GetTranslationJobsQuery, ErrorOr<List<TranslationJob>>>
{
    private readonly ITranslationJobRepository _jobRepository;

    public GetTranslationJobsQueryHandler(ITranslationJobRepository jobRepository)
    {
        _jobRepository = jobRepository;
    }

    public async Task<ErrorOr<List<TranslationJob>>> Handle(GetTranslationJobsQuery query, CancellationToken cancellationToken)
    {
        return await _jobRepository.GetAllAsync(cancellationToken);
    }
}
