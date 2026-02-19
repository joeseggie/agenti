using System.Net.Http.Json;

namespace EastSeat.Agenti.Android.Features.Auth;

/// <summary>
/// Implementation of auth service for Android that communicates with the Agenti web API.
/// </summary>
public class AuthService : IAuthService
{
    private readonly HttpClient _httpClient;
    private UserSession _currentUser = new();

    public UserSession CurrentUser => _currentUser;

    public event Action? AuthStateChanged;

    public AuthService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/mobile/auth/login", request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return LoginResponse.Error(string.IsNullOrWhiteSpace(error)
                    ? "Login failed. Please check your credentials."
                    : error);
            }

            var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
            if (result == null || !result.Success)
            {
                return LoginResponse.Error(result?.ErrorMessage ?? "Login failed.");
            }

            _currentUser = new UserSession
            {
                UserId = result.UserId ?? string.Empty,
                FullName = result.FullName ?? string.Empty,
                Email = result.Email ?? string.Empty,
                Role = result.Role ?? string.Empty,
                IsAuthenticated = true
            };

            AuthStateChanged?.Invoke();
            return result;
        }
        catch (HttpRequestException ex)
        {
            return LoginResponse.Error($"Unable to connect to server: {ex.Message}");
        }
        catch (Exception ex)
        {
            return LoginResponse.Error($"An unexpected error occurred: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task LogoutAsync()
    {
        try
        {
            await _httpClient.PostAsync("api/mobile/auth/logout", null);
        }
        catch
        {
            // Ignore errors on logout - clear session regardless
        }
        finally
        {
            _currentUser = new UserSession();
            AuthStateChanged?.Invoke();
        }
    }
}
