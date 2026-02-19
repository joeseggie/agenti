namespace EastSeat.Agenti.Web.Features.Auth;

/// <summary>
/// Request model for mobile login.
/// </summary>
public class MobileLoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// Response model for mobile login.
/// </summary>
public class MobileLoginResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? UserId { get; set; }
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Role { get; set; }
}
