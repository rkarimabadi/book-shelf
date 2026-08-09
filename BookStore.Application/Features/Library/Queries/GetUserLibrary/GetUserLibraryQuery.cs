using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Authentication;
using BookStore.Core.Domain.Books;
using ErrorOr;
using FluentValidation;
using MediatR;

namespace BookStore.Application.Features.Library.Queries.GetUserLibrary;

public record GetUserLibraryQuery(Guid UserId) : IRequest<ErrorOr<List<(Book Book, DateTime AddedAt)>>>;

public class GetUserLibraryQueryValidator : AbstractValidator<GetUserLibraryQuery>
{
    public GetUserLibraryQueryValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User id is required.");
    }
}

public class GetUserLibraryQueryHandler : IRequestHandler<GetUserLibraryQuery, ErrorOr<List<(Book Book, DateTime AddedAt)>>>
{
    private readonly IUserRepository _userRepository;

    public GetUserLibraryQueryHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<ErrorOr<List<(Book Book, DateTime AddedAt)>>> Handle(GetUserLibraryQuery query, CancellationToken cancellationToken)
    {
        return await _userRepository.GetLibraryBooksAsync(query.UserId, cancellationToken);
    }
}
