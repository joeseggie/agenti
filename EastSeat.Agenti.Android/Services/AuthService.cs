using System.Text.Json;
using EastSeat.Agenti.Android.Models;

namespace EastSeat.Agenti.Android.Services;

/// <summary>
/// Manages authentication state and securely stores the JWT token using Android Keystore
/// (via MAUI SecureStorage, which wraps Android Keystore on Android 6+ and
/// encrypted SharedPreferences on Android 5.x).
/// </summary>
public class AuthService : IAuthService
{
    private const string TokenKey = "agenti_jwt_token";
    private const string UserKey = "agenti_user_info";

    private readonly IApiService _apiService;

    public bool IsLoggedIn => CurrentUser is not null;
    public LoginResponse? CurrentUser { get; private set; }

    public AuthService(IApiService apiService)
    {
        _apiService = apiService;
    }

    public async Task<bool> LoginAsync(string email, string password)
    {
        var result = await _apiService.LoginAsync(email, password);
        if (result?.Success != true || result.Data is null)
            return false;

        CurrentUser = result.Data;

        // Store token securely using Android Keystore / SecureStorage
        await SecureStorage.Default.SetAsync(TokenKey, result.Data.AccessToken);
        await SecureStorage.Default.SetAsync(UserKey,
            JsonSerializer.Serialize(result.Data));

        // Set the token on the HTTP client for subsequent requests
        if (_apiService is ApiService apiService)
            apiService.SetAuthorizationToken(result.Data.AccessToken);

        return true;
    }

    public async Task LogoutAsync()
    {
        CurrentUser = null;
        SecureStorage.Default.Remove(TokenKey);
        SecureStorage.Default.Remove(UserKey);

        if (_apiService is ApiService apiService)
            apiService.ClearAuthorizationToken();

        await Task.CompletedTask;
    }

    public async Task<bool> TryRestoreSessionAsync()
    {
        try
        {
            var token = await SecureStorage.Default.GetAsync(TokenKey);
            var userJson = await SecureStorage.Default.GetAsync(UserKey);

            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(userJson))
                return false;

            CurrentUser = JsonSerializer.Deserialize<LoginResponse>(userJson);
            if (CurrentUser is null)
                return false;

            if (_apiService is ApiService apiService)
                apiService.SetAuthorizationToken(token);

            return true;
        }
        catch
        {
            return false;
        }
    }
}
