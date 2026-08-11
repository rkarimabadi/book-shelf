using BookStore.Contracts.Users;

namespace BookStore.UI.Features.Admin.Services;

/// <summary>Result of an admin user mutation.</summary>
public sealed record AdminUserResult(
    bool Success,
    string? Message = null,
    bool Unauthorized = false,
    bool Forbidden = false);

public interface IAdminUserService
{
    Task<IReadOnlyList<UserResponse>?> GetUsersAsync(CancellationToken cancellationToken = default);

    Task<AdminUserResult> SetUserStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);

    Task<AdminUserResult> SetUserRoleAsync(Guid id, string role, CancellationToken cancellationToken = default);

    Task<AdminUserResult> DeleteUserAsync(Guid id, CancellationToken cancellationToken = default);
}
