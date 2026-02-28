using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.Users;

public class UserService(ApplicationDbContext db, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager) : IUserService
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

    public async Task<CreateUserResult> CreateUserAsync(CreateUserModel model, string? performedByUserId, CancellationToken cancellationToken = default)
    {
        if (model is null) return CreateUserResult.Error("Invalid request");

        // Validate email uniqueness
        var existingUser = await userManager.FindByEmailAsync(model.Email);
        if (existingUser != null)
            return CreateUserResult.Error($"A user with email '{model.Email}' already exists.");

        // Generate temporary password
        var temporaryPassword = GenerateTemporaryPassword();

        // Validate branch exists
        var branchExists = await db.Branches.AnyAsync(b => b.Id == model.BranchId, cancellationToken);
        if (!branchExists)
            return CreateUserResult.Error($"Branch with ID {model.BranchId} does not exist.");

        // Create the new user
        var newUser = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            FirstName = model.FirstName.Trim(),
            LastName = model.LastName.Trim(),
            PhoneNumber = model.PhoneNumber,
            Role = model.Role,
            BranchId = model.BranchId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var createResult = await userManager.CreateAsync(newUser, temporaryPassword);
        if (!createResult.Succeeded)
        {
            var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
            return CreateUserResult.Error($"Failed to create user: {errors}");
        }

        // Ensure the role exists in Identity before assigning
        var roleName = model.Role.ToString();
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            var roleResult = await roleManager.CreateAsync(new IdentityRole(roleName));
            if (!roleResult.Succeeded)
            {
                await userManager.DeleteAsync(newUser);
                var errors = string.Join(", ", roleResult.Errors.Select(e => e.Description));
                return CreateUserResult.Error($"Failed to create role '{roleName}': {errors}");
            }
        }

        // Assign role
        var addRoleResult = await userManager.AddToRoleAsync(newUser, roleName);
        if (!addRoleResult.Succeeded)
        {
            await userManager.DeleteAsync(newUser);
            var errors = string.Join(", ", addRoleResult.Errors.Select(e => e.Description));
            return CreateUserResult.Error($"Failed to assign role '{roleName}': {errors}");
        }

        // Generate invite token
        var inviteToken = GenerateInviteToken(newUser.Id);

        // Log creation audit entry
        db.UserAuditLogs.Add(new UserAuditLog
        {
            UserId = newUser.Id,
            Action = UserAuditAction.Created,
            OldValue = null,
            NewValue = $"Created with role {model.Role}",
            PerformedByUserId = performedByUserId,
            PerformedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);

        return CreateUserResult.Ok(newUser.Id, temporaryPassword, inviteToken);
    }

    private static string GenerateTemporaryPassword()
    {
        // Generate a strong temporary password (16 characters) that meets password policy requirements
        const string alphanumeric = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        const string nonAlphanumeric = "!@#$%";
        var random = new System.Random();

        // Ensure at least one non-alphanumeric character
        var passwordChars = new List<char>();

        // Add one random special character
        passwordChars.Add(nonAlphanumeric[random.Next(nonAlphanumeric.Length)]);

        // Fill the rest with random characters from the full set
        var allChars = alphanumeric + nonAlphanumeric;
        for (int i = 1; i < 16; i++)
        {
            passwordChars.Add(allChars[random.Next(allChars.Length)]);
        }

        // Shuffle the password characters to avoid having special char at the beginning
        for (int i = passwordChars.Count - 1; i > 0; i--)
        {
            int randomIndex = random.Next(i + 1);
            var temp = passwordChars[i];
            passwordChars[i] = passwordChars[randomIndex];
            passwordChars[randomIndex] = temp;
        }

        return new string(passwordChars.ToArray());
    }

    private static string GenerateInviteToken(string userId)
    {
        // Simple invite token: Base64 encoded userId + timestamp
        var timestamp = DateTime.UtcNow.Ticks;
        var token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{userId}:{timestamp}"));
        return token;
    }

    public async Task<ServiceResult> UpdateProfileAsync(UserFormModel model, string? performedByUserId, CancellationToken cancellationToken = default)
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

    public async Task<ServiceResult> ChangeRoleAsync(string userId, UserRole newRole, string? performedByUserId, CancellationToken cancellationToken = default)
    {
        if (userId == performedByUserId)
            return new(false, "You cannot change your own role.");

        var user = await userManager.FindByIdAsync(userId);
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
        var oldRoleName = user.Role.ToString();
        var newRoleName = newRole.ToString();

        // Remove from old Identity role
        var removeResult = await userManager.RemoveFromRoleAsync(user, oldRoleName);
        if (!removeResult.Succeeded)
        {
            var errors = string.Join(", ", removeResult.Errors.Select(e => e.Description));
            return new(false, $"Failed to remove old role: {errors}");
        }

        // Add to new Identity role
        var addResult = await userManager.AddToRoleAsync(user, newRoleName);
        if (!addResult.Succeeded)
        {
            var errors = string.Join(", ", addResult.Errors.Select(e => e.Description));
            // Try to restore old role
            await userManager.AddToRoleAsync(user, oldRoleName);
            return new(false, $"Failed to add new role: {errors}");
        }

        // Update custom role property
        user.Role = newRole;
        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

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

    public async Task<ServiceResult> DeactivateAsync(string userId, string? performedByUserId, CancellationToken cancellationToken = default)
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

    public async Task<ServiceResult> ReactivateAsync(string userId, string? performedByUserId, CancellationToken cancellationToken = default)
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

    public async Task<ServiceResult> DeleteAsync(string userId, string? performedByUserId, CancellationToken cancellationToken = default)
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

    public async Task<ResetPasswordResult> ResetPasswordAsync(string userId, string? performedByUserId, CancellationToken cancellationToken = default)
    {
        if (userId == performedByUserId)
            return ResetPasswordResult.Error("You cannot reset your own password.");

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return ResetPasswordResult.Error("User not found");

        // Generate new temporary password
        var newPassword = GenerateTemporaryPassword();

        // Remove existing password and set new one
        var removePasswordResult = await userManager.RemovePasswordAsync(user);
        if (!removePasswordResult.Succeeded)
        {
            var errors = string.Join(", ", removePasswordResult.Errors.Select(e => e.Description));
            return ResetPasswordResult.Error($"Failed to reset password: {errors}");
        }

        var addPasswordResult = await userManager.AddPasswordAsync(user, newPassword);
        if (!addPasswordResult.Succeeded)
        {
            var errors = string.Join(", ", addPasswordResult.Errors.Select(e => e.Description));
            return ResetPasswordResult.Error($"Failed to set new password: {errors}");
        }

        // Update timestamp
        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        // Log password reset audit entry
        db.UserAuditLogs.Add(new UserAuditLog
        {
            UserId = user.Id,
            Action = UserAuditAction.PasswordReset,
            OldValue = null,
            NewValue = "Password reset by admin",
            PerformedByUserId = performedByUserId,
            PerformedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);

        return ResetPasswordResult.Ok(newPassword);
    }
}
