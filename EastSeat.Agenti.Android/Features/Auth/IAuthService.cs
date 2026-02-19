namespace EastSeat.Agenti.Android.Features.Auth;

/// <summary>
/// Service interface for authentication operations on Android.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Gets the current user session.
    /// </summary>
    UserSession CurrentUser { get; }

    /// <summary>
    /// Logs the user in with the provided credentials.
    /// </summary>
    Task<LoginResponse> LoginAsync(LoginRequest request);

    /// <summary>
    /// Logs the current user out.
    /// </summary>
    Task LogoutAsync();

    /// <summary>
    /// Event raised when authentication state changes.
    /// </summary>
    event Action? AuthStateChanged;
}
