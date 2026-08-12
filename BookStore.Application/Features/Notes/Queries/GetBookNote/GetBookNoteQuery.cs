using BookStore.Core.Domain.Books;
using ErrorOr;
using MediatR;

namespace BookStore.Application.Features.Notes.Queries.GetBookNote;

public record GetBookNoteQuery(Guid UserId, Guid BookId) : IRequest<ErrorOr<string>>;

public class GetBookNoteQueryHandler : IRequestHandler<GetBookNoteQuery, ErrorOr<string>>
{
    private readonly IBookNoteRepository _bookNoteRepository;

    public GetBookNoteQueryHandler(IBookNoteRepository bookNoteRepository)
    {
        _bookNoteRepository = bookNoteRepository;
    }

    public async Task<ErrorOr<string>> Handle(GetBookNoteQuery query, CancellationToken cancellationToken)
    {
        var note = await _bookNoteRepository.GetByUserAndBookAsync(query.UserId, query.BookId, cancellationToken);
        return note?.Note ?? string.Empty;
    }
}
