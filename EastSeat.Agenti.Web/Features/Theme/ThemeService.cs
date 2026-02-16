using EastSeat.Agenti.Web.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.Theme;

public class ThemeService : IThemeService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly AuthenticationStateProvider _authStateProvider;
    private string _effectiveTheme = "light";
    private string _userPreference = ThemePreferenceConstants.System;
    private string? _systemPreference = null;

    public event EventHandler? ThemeChanged;
    public bool IsDarkMode => _effectiveTheme == "dark";

    public ThemeService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        AuthenticationStateProvider authStateProvider)
    {
        _dbFactory = dbFactory;
        _authStateProvider = authStateProvider;
    }

    public async Task InitializeAsync(string? systemPreference)
    {
        _systemPreference = systemPreference;

        var authState = await _authStateProvider.GetAuthenticationStateAsync();
        var user = authState.User;

        if (user.Identity?.IsAuthenticated != true)
        {
            // Anonymous users: use system preference
            _effectiveTheme = systemPreference ?? "light";
            return;
        }

        var userId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
        {
            _effectiveTheme = systemPreference ?? "light";
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var appUser = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (appUser == null)
        {
            _effectiveTheme = systemPreference ?? "light";
            return;
        }

        _userPreference = appUser.ThemePreference ?? ThemePreferenceConstants.System;
        _effectiveTheme = ResolveEffectiveTheme(_userPreference, systemPreference);
    }

    public Task<string> GetEffectiveThemeAsync()
    {
        return Task.FromResult(_effectiveTheme);
    }

    public Task<string> GetUserPreferenceAsync()
    {
        return Task.FromResult(_userPreference);
    }

    public async Task<bool> SetUserPreferenceAsync(string preference)
    {
        // Validate preference
        if (preference != ThemePreferenceConstants.Light &&
            preference != ThemePreferenceConstants.Dark &&
            preference != ThemePreferenceConstants.System)
        {
            return false;
        }

        var authState = await _authStateProvider.GetAuthenticationStateAsync();
        var userId = authState.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (userId == null) return false; // Anonymous users can't save preferences

        await using var db = await _dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return false;

        // Store null for system preference, actual value for light/dark
        user.ThemePreference = preference == ThemePreferenceConstants.System ? null : preference;
        user.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        _userPreference = preference;
        _effectiveTheme = ResolveEffectiveTheme(preference, _systemPreference);

        ThemeChanged?.Invoke(this, EventArgs.Empty);

        return true;
    }

    private static string ResolveEffectiveTheme(string userPreference, string? systemPreference)
    {
        return userPreference switch
        {
            ThemePreferenceConstants.Light => "light",
            ThemePreferenceConstants.Dark => "dark",
            ThemePreferenceConstants.System => systemPreference ?? "light",
            _ => "light"
        };
    }
}
