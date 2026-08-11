namespace BookStore.Contracts.Users;

public record UserResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string Role,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? LastLoginAt);

public record SetUserStatusRequest(
    bool IsActive);

public record SetUserRoleRequest(
    string Role);
