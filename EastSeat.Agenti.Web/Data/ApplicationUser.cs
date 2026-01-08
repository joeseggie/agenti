using Microsoft.AspNetCore.Identity;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Domain.Entities;

namespace EastSeat.Agenti.Web.Data;

/// <summary>
/// Extended application user with Agenti-specific properties.
/// Not all users are agents - admins and supervisors may not have an associated Agent.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public long? AgentId { get; set; }
    public long? BranchId { get; set; }
    public UserRole Role { get; set; } = UserRole.Agent;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Full name of the user.
    /// </summary>
    public string FullName => $"{FirstName} {LastName}".Trim();

    // Navigation property - optional, only set for users who are agents
    public Agent? Agent { get; set; }
}

