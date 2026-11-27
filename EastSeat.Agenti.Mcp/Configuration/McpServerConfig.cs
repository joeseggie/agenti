using System.ComponentModel.DataAnnotations;

namespace EastSeat.Agenti.Mcp.Configuration;

public sealed class McpServerConfig
{
    public const string SectionName = "Agenti";

    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    [Range(1, 10000)]
    public int MaxRows { get; set; } = 1000;

    [Range(1, 300)]
    public int CommandTimeoutSeconds { get; set; } = 30;

    [Range(1, 1000)]
    public int RequestsPerMinute { get; set; } = 30;

    [Range(1, 32)]
    public int MaxConcurrentRequests { get; set; } = 2;
}
