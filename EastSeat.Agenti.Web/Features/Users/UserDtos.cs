using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Web.Features.Users;

public record UserListItemDto(
    string Id,
    string Email,
    string? PhoneNumber,
    string FullName,
    UserRole Role,
    bool IsActive,
    long? AgentId,
    long? BranchId
);

public class UserDetailDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}".Trim();
    public UserRole Role { get; set; }
    public bool IsActive { get; set; }
    public long? AgentId { get; set; }
    public long? BranchId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class UserFormModel
{
    public string Id { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public UserRole Role { get; set; }
    public bool IsActive { get; set; }
}

public class CreateUserModel
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
}

public record ServiceResult(bool Success, string? Message = null);

public class CreateUserResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? UserId { get; set; }
    public string? TemporaryPassword { get; set; }
    public string? InviteToken { get; set; }

    public static CreateUserResult Ok(string userId, string password, string token) =>
        new() { Success = true, UserId = userId, TemporaryPassword = password, InviteToken = token };

    public static CreateUserResult Error(string message) =>
        new() { Success = false, Message = message };
}
