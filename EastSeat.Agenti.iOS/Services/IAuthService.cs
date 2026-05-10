using EastSeat.Agenti.iOS.Models;

namespace EastSeat.Agenti.iOS.Services;

/// <summary>
/// Interface for managing authentication state and JWT token storage.
/// </summary>
public interface IAuthService
{
    bool IsLoggedIn { get; }
    LoginResponse? CurrentUser { get; }

    Task<bool> LoginAsync(string email, string password);
    Task LogoutAsync();
    Task<bool> TryRestoreSessionAsync();
}
