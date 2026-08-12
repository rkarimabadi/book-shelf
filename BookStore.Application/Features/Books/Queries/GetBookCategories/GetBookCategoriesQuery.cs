using BookStore.Core.Domain.Books;
using ErrorOr;
using MediatR;

namespace BookStore.Application.Features.Books.Queries.GetBookCategories;

/// <summary>Returns the fixed category catalog (the values are the Persian labels themselves).</summary>
public record GetBookCategoriesQuery() : IRequest<ErrorOr<List<string>>>;

public class GetBookCategoriesQueryHandler : IRequestHandler<GetBookCategoriesQuery, ErrorOr<List<string>>>
{
    public Task<ErrorOr<List<string>>> Handle(GetBookCategoriesQuery query, CancellationToken cancellationToken)
    {
        return Task.FromResult<ErrorOr<List<string>>>(BookCategories.All.ToList());
    }
}
