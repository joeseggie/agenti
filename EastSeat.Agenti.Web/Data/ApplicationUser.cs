using Microsoft.AspNetCore.Identity;
using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Web.Data;

/// <summary>
/// Extended application user with Agenti-specific properties.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public long? AgentId { get; set; }
    public long? BranchId { get; set; }
    public UserRole Role { get; set; } = UserRole.Agent;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

