using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.Users;

public class UserService(ApplicationDbContext db) : IUserService
{
    public async Task<List<UserListItemDto>> GetAllAsync(string? search = null, CancellationToken cancellationToken = default)
    {
        var query = db.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            query = query.Where(u =>
                (u.Email != null && u.Email.ToLower().Contains(s)) ||
                (u.PhoneNumber != null && u.PhoneNumber.ToLower().Contains(s)) ||
                u.FirstName.ToLower().Contains(s) ||
                u.LastName.ToLower().Contains(s));
        }

        return await query
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .Select(u => new UserListItemDto(
                u.Id,
                u.Email ?? string.Empty,
                u.PhoneNumber,
                u.FullName,
                u.Role,
                u.IsActive,
                u.AgentId,
                u.BranchId))
            .ToListAsync(cancellationToken);
    }

    public async Task<UserDetailDto?> GetByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new UserDetailDto
            {
                Id = u.Id,
                Email = u.Email ?? string.Empty,
                PhoneNumber = u.PhoneNumber,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Role = u.Role,
                IsActive = u.IsActive,
                AgentId = u.AgentId,
                BranchId = u.BranchId,
                CreatedAt = u.CreatedAt,
                UpdatedAt = u.UpdatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ServiceResult> UpdateProfileAsync(UserFormModel model, string performedByUserId, CancellationToken cancellationToken = default)
    {
        if (model is null) return new(false, "Invalid request");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == model.Id, cancellationToken);
        if (user is null) return new(false, "User not found");

        // Self-edit of profile is allowed here (admin portal), no special restrictions.
        user.FirstName = model.FirstName.Trim();
        user.LastName = model.LastName.Trim();
        user.PhoneNumber = model.PhoneNumber;
        user.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return new(true);
    }

    public async Task<ServiceResult> ChangeRoleAsync(string userId, UserRole newRole, string performedByUserId, CancellationToken cancellationToken = default)
    {
        if (userId == performedByUserId)
            return new(false, "You cannot change your own role.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null) return new(false, "User not found");

        if (user.Role == newRole) return new(true);

        // Last admin protection: if demoting an admin and it's the last one
        if (user.Role == UserRole.Admin && newRole != UserRole.Admin)
        {
            var adminCount = await db.Users.CountAsync(u => u.Role == UserRole.Admin && u.IsActive, cancellationToken);
            if (adminCount <= 1)
                return new(false, "Cannot remove the last remaining Admin.");
        }

        var oldValue = user.Role.ToString();
        user.Role = newRole;
        user.UpdatedAt = DateTime.UtcNow;

        db.UserAuditLogs.Add(new UserAuditLog
        {
            UserId = user.Id,
            Action = UserAuditAction.RoleChanged,
            OldValue = oldValue,
            NewValue = newRole.ToString(),
            PerformedByUserId = performedByUserId,
            PerformedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
        return new(true);
    }

    public async Task<ServiceResult> DeactivateAsync(string userId, string performedByUserId, CancellationToken cancellationToken = default)
    {
        if (userId == performedByUserId)
            return new(false, "You cannot deactivate your own account.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null) return new(false, "User not found");
        if (!user.IsActive) return new(true);

        // Last admin protection: cannot deactivate the last active admin
        if (user.Role == UserRole.Admin)
        {
            var adminCount = await db.Users.CountAsync(u => u.Role == UserRole.Admin && u.IsActive, cancellationToken);
            if (adminCount <= 1)
                return new(false, "Cannot deactivate the last remaining Admin.");
        }

        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;

        db.UserAuditLogs.Add(new UserAuditLog
        {
            UserId = user.Id,
            Action = UserAuditAction.Deactivated,
            OldValue = "true",
            NewValue = "false",
            PerformedByUserId = performedByUserId,
            PerformedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
        return new(true);
    }

    public async Task<ServiceResult> ReactivateAsync(string userId, string performedByUserId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null) return new(false, "User not found");
        if (user.IsActive) return new(true);

        user.IsActive = true;
        user.UpdatedAt = DateTime.UtcNow;

        db.UserAuditLogs.Add(new UserAuditLog
        {
            UserId = user.Id,
            Action = UserAuditAction.Reactivated,
            OldValue = "false",
            NewValue = "true",
            PerformedByUserId = performedByUserId,
            PerformedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
        return new(true);
    }

    public async Task<ServiceResult> DeleteAsync(string userId, string performedByUserId, CancellationToken cancellationToken = default)
    {
        if (userId == performedByUserId)
            return new(false, "You cannot delete your own account.");

        var user = await db.Users.Include(u => u.Agent).FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null) return new(false, "User not found");

        // Last admin protection
        if (user.Role == UserRole.Admin)
        {
            var adminCount = await db.Users.CountAsync(u => u.Role == UserRole.Admin && u.IsActive, cancellationToken);
            if (adminCount <= 1)
                return new(false, "Cannot delete the last remaining Admin.");
        }

        // If user is linked to an agent, prevent deletion for now to keep integrity
        if (user.Agent != null)
        {
            return new(false, "Cannot delete a user linked to an Agent. Please deactivate instead or unlink via data migration.");
        }

        db.Users.Remove(user);

        db.UserAuditLogs.Add(new UserAuditLog
        {
            UserId = user.Id,
            Action = UserAuditAction.Deleted,
            OldValue = null,
            NewValue = null,
            PerformedByUserId = performedByUserId,
            PerformedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
        return new(true);
    }
}
