namespace EastSeat.Agenti.Shared.Domain.Entities;

public sealed class McpAccessToken
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid RequestId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public string SecurityStamp { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedByUserId { get; set; }
}
