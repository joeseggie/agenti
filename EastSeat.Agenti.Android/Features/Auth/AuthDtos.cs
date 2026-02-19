namespace EastSeat.Agenti.Android.Features.Auth;

/// <summary>
/// Request model for login.
/// </summary>
public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// Response model for login.
/// </summary>
public class LoginResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? UserId { get; set; }
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Role { get; set; }

    public static LoginResponse Ok(string userId, string fullName, string email, string role) => new()
    {
        Success = true,
        UserId = userId,
        FullName = fullName,
        Email = email,
        Role = role
    };

    public static LoginResponse Error(string message) => new()
    {
        Success = false,
        ErrorMessage = message
    };
}

/// <summary>
/// Stores current user session state on the Android device.
/// </summary>
public class UserSession
{
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsAuthenticated { get; set; }
}
