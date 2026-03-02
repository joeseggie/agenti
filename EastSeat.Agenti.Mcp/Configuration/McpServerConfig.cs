namespace EastSeat.Agenti.Mcp.Configuration;

/// <summary>
/// Configuration for the Agenti MCP server.
/// Values are loaded from environment variables (AGENTI__*) or appsettings.json.
/// </summary>
public class McpServerConfig
{
    public const string SectionName = "Agenti";

    /// <summary>
    /// PostgreSQL connection string (must use the read-only role).
    /// Format: Server=agenti-pgserver.postgres.database.azure.com;Port=5432;Database=agenti_prod;User Id=agenti_readonly;Password=...;Ssl Mode=Require;
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Branch ID for data isolation. Agents see only their branch data.
    /// Set to 0 or null for supervisors/admins who can see all branches.
    /// </summary>
    public long? BranchId { get; set; }

    /// <summary>
    /// User role for authorization: Agent, Supervisor, or Admin.
    /// Controls which branches the MCP user can query.
    /// </summary>
    public string UserRole { get; set; } = "Agent";

    /// <summary>
    /// Maximum rows returned per query (safety limit).
    /// </summary>
    public int MaxRows { get; set; } = 1000;

    /// <summary>
    /// Command timeout in seconds for database queries.
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Whether this user can query across all branches.
    /// </summary>
    public bool CanQueryAllBranches =>
        UserRole.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
        UserRole.Equals("Supervisor", StringComparison.OrdinalIgnoreCase);
}
