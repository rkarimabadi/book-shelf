using System.Net;
using System.Net.Http.Json;
using BookStore.Contracts.Users;
using BookStore.UI.Services;

namespace BookStore.UI.Features.Admin.Services;

public sealed class AdminUserService : IAdminUserService
{
    private readonly HttpClient _http;

    public AdminUserService(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<UserResponse>?> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<UserResponse>>("api/users", cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<AdminUserResult> SetUserStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PatchAsJsonAsync(
                $"api/users/{id}/status",
                new SetUserStatusRequest(isActive),
                cancellationToken);
            return await ToResultAsync(response);
        }
        catch
        {
            return new AdminUserResult(false, "ارتباط با سرور برقرار نشد؛ دوباره تلاش کنید.");
        }
    }

    public async Task<AdminUserResult> SetUserRoleAsync(Guid id, string role, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PatchAsJsonAsync(
                $"api/users/{id}/role",
                new SetUserRoleRequest(role),
                cancellationToken);
            return await ToResultAsync(response);
        }
        catch
        {
            return new AdminUserResult(false, "ارتباط با سرور برقرار نشد؛ دوباره تلاش کنید.");
        }
    }

    public async Task<AdminUserResult> DeleteUserAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.DeleteAsync($"api/users/{id}", cancellationToken);
            return await ToResultAsync(response);
        }
        catch
        {
            return new AdminUserResult(false, "ارتباط با سرور برقرار نشد؛ دوباره تلاش کنید.");
        }
    }

    public async Task<AdminUserResult> ResetUserPasswordAsync(Guid id, string newPassword, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PatchAsJsonAsync(
                $"api/users/{id}/password",
                new ResetUserPasswordRequest(newPassword),
                cancellationToken);
            return await ToResultAsync(response);
        }
        catch
        {
            return new AdminUserResult(false, "ارتباط با سرور برقرار نشد؛ دوباره تلاش کنید.");
        }
    }

    private static async Task<AdminUserResult> ToResultAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return new AdminUserResult(true);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new AdminUserResult(false, "نشست شما منقضی شده است؛ لطفاً دوباره وارد شوید.", Unauthorized: true);
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return new AdminUserResult(false, "شما مجوز لازم برای این عملیات را ندارید.", Forbidden: true);
        }

        return new AdminUserResult(false, await ReadProblemMessageAsync(response));
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

    // The server surfaces business errors as title = English description, detail = error code, so
    // match on either form (descriptions are prefix/contains-matched because they embed dynamic ids).
    private static string MapPersian(string message)
    {
        if (message == "User.NotFound" || message.StartsWith("User with id", StringComparison.Ordinal))
        {
            return "کاربر پیدا نشد.";
        }

        if (message == "User.CannotModifySelf" || message.Contains("own account", StringComparison.OrdinalIgnoreCase))
        {
            return "شما نمی‌توانید حساب خودتان را تغییر دهید.";
        }

        if (message == "User.RoleAlreadySet" || message.Contains("already has the", StringComparison.OrdinalIgnoreCase))
        {
            return "نقش کاربر از قبل همان است.";
        }

        if (message == "User.Inactive" || message.Contains("is inactive", StringComparison.OrdinalIgnoreCase))
        {
            return "حساب کاربر غیرفعال است؛ ابتدا آن را فعال کنید.";
        }

        if (message == "Role is invalid.")
        {
            return "نقش معتبر نیست.";
        }

        // Defensive: the dialog validates ≥ 8 chars client-side, but the server can still return this.
        if (message == "Password must be at least 8 characters long.")
        {
            return "رمز عبور باید حداقل ۸ کاراکتر باشد.";
        }

        return message;
    }
}
