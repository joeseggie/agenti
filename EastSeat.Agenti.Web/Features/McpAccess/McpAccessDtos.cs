using System.ComponentModel.DataAnnotations;
using EastSeat.Agenti.Shared.Security;

namespace EastSeat.Agenti.Web.Features.McpAccess;

public sealed class CreateMcpTokenRequest
{
    public Guid RequestId { get; set; } = Guid.NewGuid();

    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Range(1, McpTokenSecret.MaxLifetimeDays)]
    public int LifetimeDays { get; set; } = McpTokenSecret.DefaultLifetimeDays;
}

public sealed class CreatedMcpToken
{
    public required long Id { get; init; }
    public required string Secret { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}

public sealed record McpTokenListItem(
    long Id, string UserId, string OwnerName, string Name,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt);

public sealed record McpTokenPage(IReadOnlyList<McpTokenListItem> Items, int TotalCount);
