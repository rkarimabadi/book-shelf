using BookStore.Core.Domain.Authentication;
using BookStore.Core.Domain.Users;
using ErrorOr;
using MediatR;

namespace BookStore.Application.Features.Users.Queries.GetUsers;

public record GetUsersQuery() : IRequest<ErrorOr<List<User>>>;

public class GetUsersQueryHandler : IRequestHandler<GetUsersQuery, ErrorOr<List<User>>>
{
    private readonly IUserRepository _userRepository;

    public GetUsersQueryHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<ErrorOr<List<User>>> Handle(GetUsersQuery query, CancellationToken cancellationToken)
    {
        return await _userRepository.GetUsersAsync(cancellationToken);
    }
}
