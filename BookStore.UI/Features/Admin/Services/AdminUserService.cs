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

    private static string MapPersian(string message) => message switch
    {
        "User.NotFound" => "کاربر پیدا نشد.",
        "User.CannotModifySelf" => "شما نمی‌توانید حساب خودتان را تغییر دهید.",
        "User.RoleAlreadySet" => "نقش کاربر از قبل همان است.",
        "Role is invalid." => "نقش معتبر نیست.",
        _ => message
    };
}
