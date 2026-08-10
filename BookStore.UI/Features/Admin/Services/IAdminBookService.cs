using Microsoft.AspNetCore.Components.Forms;

namespace BookStore.UI.Features.Admin.Services;

/// <summary>Result of an admin book mutation.</summary>
public sealed record AdminBookResult(
    bool Success,
    string? Message = null,
    bool Unauthorized = false,
    bool Forbidden = false);

/// <summary>Payload collected by <c>BookForm</c> and sent to the API as multipart form data.</summary>
public sealed record BookFormSubmit(
    string Title,
    string Author,
    string Description,
    IBrowserFile? CoverFile,
    IBrowserFile? BookFile);

public interface IAdminBookService
{
    Task<AdminBookResult> CreateBookAsync(BookFormSubmit form, CancellationToken cancellationToken = default);

    Task<AdminBookResult> UpdateBookAsync(Guid id, BookFormSubmit form, CancellationToken cancellationToken = default);

    Task<AdminBookResult> DeleteBookAsync(Guid id, CancellationToken cancellationToken = default);
}
