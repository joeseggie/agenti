namespace EastSeat.Agenti.Web.Features.Theme;

public interface IThemeService
{
    /// <summary>
    /// Gets the current effective theme (resolved from user preference and system preference).
    /// </summary>
    Task<string> GetEffectiveThemeAsync();

    /// <summary>
    /// Gets the user's saved theme preference (light, dark, or system).
    /// </summary>
    Task<string> GetUserPreferenceAsync();

    /// <summary>
    /// Sets the user's theme preference and persists to database.
    /// </summary>
    Task<bool> SetUserPreferenceAsync(string preference);

    /// <summary>
    /// Gets whether dark mode is currently active.
    /// </summary>
    bool IsDarkMode { get; }

    /// <summary>
    /// Event raised when theme changes.
    /// </summary>
    event EventHandler? ThemeChanged;

    /// <summary>
    /// Initializes the theme service (loads user preference, detects system theme).
    /// </summary>
    Task InitializeAsync(string? systemPreference);
}
