using System.Net;
using System.Net.Http.Json;
using BookStore.Contracts.Books;
using BookStore.UI.Services;
using Microsoft.AspNetCore.Components.Forms;

namespace BookStore.UI.Features.Admin.Services;

public sealed class AdminBookService : IAdminBookService
{
    // Client-side upload caps (mirrors the server's ~30 MB Kestrel request-body default).
    public const long CoverMaxBytes = 5 * 1024 * 1024; // 5 MB
    public const long BookMaxBytes = 25 * 1024 * 1024; // 25 MB

    private readonly HttpClient _http;

    public AdminBookService(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<BookResponse>?> GetBooksAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // The list endpoint is paged; the admin page shows the full catalog (incl. inactive),
            // so grab the first page with a generous page size.
            var paged = await _http.GetFromJsonAsync<PagedBooksResponse>(
                "api/books?includeInactive=true&pageSize=500",
                cancellationToken);
            return paged?.Items;
        }
        catch
        {
            return null;
        }
    }

    public async Task<AdminBookResult> CreateBookAsync(
        BookFormSubmit form,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = BuildMultipart(form);
            var response = await _http.PostAsync("api/books", content, cancellationToken);
            return await ToResultAsync(response);
        }
        catch
        {
            return new AdminBookResult(false, "ارتباط با سرور برقرار نشد؛ دوباره تلاش کنید.");
        }
    }

    public async Task<AdminBookResult> UpdateBookAsync(
        Guid id,
        BookFormSubmit form,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = BuildMultipart(form);
            var response = await _http.PutAsync($"api/books/{id}", content, cancellationToken);
            return await ToResultAsync(response);
        }
        catch
        {
            return new AdminBookResult(false, "ارتباط با سرور برقرار نشد؛ دوباره تلاش کنید.");
        }
    }

    public async Task<AdminBookResult> SetBookStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PatchAsJsonAsync(
                $"api/books/{id}/status",
                new UpdateBookStatusRequest(isActive),
                cancellationToken);
            return await ToResultAsync(response);
        }
        catch
        {
            return new AdminBookResult(false, "ارتباط با سرور برقرار نشد؛ دوباره تلاش کنید.");
        }
    }

    public async Task<AdminBookResult> DeleteBookAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.DeleteAsync($"api/books/{id}", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new AdminBookResult(true);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new AdminBookResult(false, "نشست شما منقضی شده است؛ لطفاً دوباره وارد شوید.", Unauthorized: true);
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return new AdminBookResult(false, "شما مجوز لازم برای این عملیات را ندارید.", Forbidden: true);
            }

            return new AdminBookResult(false, await ReadProblemMessageAsync(response));
        }
        catch
        {
            return new AdminBookResult(false, "ارتباط با سرور برقرار نشد؛ دوباره تلاش کنید.");
        }
    }

    private static MultipartFormDataContent BuildMultipart(BookFormSubmit form)
    {
        var content = new MultipartFormDataContent();

        content.Add(new StringContent(form.Title), "title");
        content.Add(new StringContent(form.Author), "author");
        content.Add(new StringContent(form.Category), "category");
        content.Add(new StringContent(form.Description ?? string.Empty), "description");

        if (form.CoverFile is not null)
        {
            var stream = form.CoverFile.OpenReadStream(CoverMaxBytes);
            content.Add(new StreamContent(stream), "coverImage", SafeFileName(form.CoverFile.Name));
        }

        if (form.BookFile is not null)
        {
            var stream = form.BookFile.OpenReadStream(BookMaxBytes);
            content.Add(new StreamContent(stream), "file", SafeFileName(form.BookFile.Name));
        }

        return content;
    }

    // The server renames uploads anyway; a safe ASCII name avoids Content-Disposition encoding issues.
    private static string SafeFileName(string? name) =>
        $"{Guid.NewGuid():N}{Path.GetExtension(name)}";

    private static async Task<AdminBookResult> ToResultAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return new AdminBookResult(true);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new AdminBookResult(false, "نشست شما منقضی شده است؛ لطفاً دوباره وارد شوید.", Unauthorized: true);
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return new AdminBookResult(false, "شما مجوز لازم برای این عملیات را ندارید.", Forbidden: true);
        }

        return new AdminBookResult(false, await ReadProblemMessageAsync(response));
    }

    private static async Task<string> ReadProblemMessageAsync(HttpResponseMessage response)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync();
            var message = ProblemDetailsParser.ReadMessage(content);
            return message is null ? "عملیات ناموفق بود؛ دوباره تلاش کنید." : MapPersian(message);
        }
        catch
        {
            return "عملیات ناموفق بود؛ دوباره تلاش کنید.";
        }
    }

    private static string MapPersian(string message) => message switch
    {
        // Validation errors surface as FluentValidation messages or ProblemDetails titles/details.
        "Title is required." => "عنوان الزامی است.",
        "Author is required." => "نویسنده الزامی است.",
        "Book file is required." => "فایل کتاب (EPUB) الزامی است.",
        "Book.FileRequired" => "فایل کتاب (EPUB) الزامی است.",
        "Category is required." => "دسته‌بندی را انتخاب کنید.",
        "Category is invalid." => "دسته‌بندی معتبر نیست.",
        "Book.InvalidCategory" => "دسته‌بندی معتبر نیست.",
        "Title must not exceed 200 characters." => "عنوان نباید بیشتر از ۲۰۰ کاراکتر باشد.",
        "Book.TitleTooLong" => "عنوان نباید بیشتر از ۲۰۰ کاراکتر باشد.",
        "Author must not exceed 100 characters." => "نام نویسنده نباید بیشتر از ۱۰۰ کاراکتر باشد.",
        "Book.AuthorTooLong" => "نام نویسنده نباید بیشتر از ۱۰۰ کاراکتر باشد.",
        "Book.Inactive" => "این کتاب در حال حاضر غیرفعال است.",
        _ when message.Contains("not found", StringComparison.OrdinalIgnoreCase) => "کتاب پیدا نشد.",
        _ when message.Contains("is deactivated", StringComparison.OrdinalIgnoreCase) => "این کتاب در حال حاضر غیرفعال است.",
        _ => message
    };
}
