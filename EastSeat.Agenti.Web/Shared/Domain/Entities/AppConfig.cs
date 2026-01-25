namespace EastSeat.Agenti.Shared.Domain.Entities;

/// <summary>
/// Application configuration settings stored in the database.
/// Used for storing application-level flags and settings.
/// </summary>
public class AppConfig
{
    /// <summary>
    /// Configuration key (Primary Key).
    /// </summary>
    public required string Key { get; set; }

    /// <summary>
    /// Configuration value.
    /// </summary>
    public string? Value { get; set; }
}
