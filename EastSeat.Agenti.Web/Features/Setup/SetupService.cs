using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Domain.Entities;

namespace EastSeat.Agenti.Web.Features.Setup;

/// <summary>
/// Service for handling initial system setup.
/// </summary>
public class SetupService : ISetupService
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<SetupService> _logger;

    public SetupService(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        SignInManager<ApplicationUser> signInManager,
        ILogger<SetupService> logger)
    {
        _context = context;
        _userManager = userManager;
        _roleManager = roleManager;
        _signInManager = signInManager;
        _logger = logger;
    }

    /// <summary>
    /// Checks if initial setup has been completed.
    /// </summary>
    public async Task<bool> IsSetupCompleteAsync()
    {
        try
        {
            var config = await _context.AppConfigs
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Key == "SetupComplete");

            var isConfigComplete = config?.Value == "true";

            var hasAdmin = await _context.Users.AnyAsync(u => u.Role == UserRole.Admin && u.IsActive);
            var hasBranch = await _context.Branches.AnyAsync();
            var hasVault = await _context.Vaults.AnyAsync();

            var isComplete = isConfigComplete && hasAdmin && hasBranch && hasVault;

            _logger.LogInformation("Setup status - ConfigComplete: {ConfigComplete}, HasAdmin: {HasAdmin}, HasBranch: {HasBranch}, HasVault: {HasVault}",
                isConfigComplete, hasAdmin, hasBranch, hasVault);

            return isComplete;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking setup status.");
            return false;
        }
    }

    /// <summary>
    /// Cleans up the database for a fresh setup start.
    /// </summary>
    public async Task CleanupDatabaseAsync()
    {
        try
        {
            _logger.LogInformation("Starting database cleanup for fresh setup...");

            // Delete all users first (outside transaction to avoid conflict with Identity)
            var usersToDelete = await _context.Users
                .Where(u => u.UserName != null)
                .ToListAsync();

            foreach (var user in usersToDelete)
            {
                var result = await _userManager.DeleteAsync(user);
                if (result.Succeeded)
                {
                    _logger.LogInformation("Deleted user: {UserId}", user.Id);
                }
                else
                {
                    _logger.LogWarning("Failed to delete user {UserId}: {Errors}", user.Id,
                        string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }

            // Now delete other entities in a transaction
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Delete in correct dependency order (children first)
                await _context.UserAuditLogs.ExecuteDeleteAsync();
                _logger.LogInformation("Deleted UserAuditLogs.");

                await _context.VaultTransactions.ExecuteDeleteAsync();
                _logger.LogInformation("Deleted VaultTransactions.");

                await _context.Discrepancies.ExecuteDeleteAsync();
                _logger.LogInformation("Deleted Discrepancies.");

                await _context.Transactions.ExecuteDeleteAsync();
                _logger.LogInformation("Deleted Transactions.");

                await _context.CashCountDetails.ExecuteDeleteAsync();
                _logger.LogInformation("Deleted CashCountDetails.");

                await _context.CashCounts.ExecuteDeleteAsync();
                _logger.LogInformation("Deleted CashCounts.");

                await _context.CashSessions.ExecuteDeleteAsync();
                _logger.LogInformation("Deleted CashSessions.");

                await _context.Wallets.ExecuteDeleteAsync();
                _logger.LogInformation("Deleted Wallets.");

                await _context.Agents.ExecuteDeleteAsync();
                _logger.LogInformation("Deleted Agents.");

                await _context.WalletTypes.ExecuteDeleteAsync();
                _logger.LogInformation("Deleted WalletTypes.");

                await _context.Vaults.ExecuteDeleteAsync();
                _logger.LogInformation("Deleted Vaults.");

                await _context.Branches.ExecuteDeleteAsync();
                _logger.LogInformation("Deleted Branches.");

                // Delete user roles assignments
                await _context.UserRoles.ExecuteDeleteAsync();
                _logger.LogInformation("Deleted UserRoles.");

                // Delete user claims
                await _context.UserClaims.ExecuteDeleteAsync();
                _logger.LogInformation("Deleted UserClaims.");

                // Delete audit logs
                await _context.AuditLogs.ExecuteDeleteAsync();
                _logger.LogInformation("Deleted AuditLogs.");

                // Reset setup flag
                var setupConfig = await _context.AppConfigs.FirstOrDefaultAsync(c => c.Key == "SetupComplete");
                if (setupConfig != null)
                {
                    setupConfig.Value = "false";
                    _context.AppConfigs.Update(setupConfig);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Reset SetupComplete flag to false.");
                }

                await transaction.CommitAsync();
                _logger.LogInformation("Database cleanup completed successfully.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error during database cleanup. Transaction rolled back.");
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error during database cleanup.");
            throw;
        }
    }

    /// <summary>
    /// Creates the initial admin user, branch, and vault.
    /// </summary>
    public async Task CreateInitialAdminAndSetupAsync(string email, string password, string firstName, string lastName, string branchName)
    {
        try
        {
            _logger.LogInformation("Starting initial admin and setup creation for email: {Email}, branch: {BranchName}",
                email, branchName);

            // If setup already complete, short-circuit
            if (await IsSetupCompleteAsync())
            {
                _logger.LogInformation("Setup already complete. Skipping creation.");
                return;
            }

            // Ensure Admin role exists (Identity handles its own transaction)
            if (!await _roleManager.RoleExistsAsync("Admin"))
            {
                var roleResult = await _roleManager.CreateAsync(new IdentityRole("Admin"));
                if (!roleResult.Succeeded)
                {
                    var errors = string.Join(", ", roleResult.Errors.Select(e => e.Description));
                    _logger.LogError("Failed to create Admin role: {Errors}", errors);
                    throw new ApplicationException($"Failed to create Admin role: {errors}");
                }
                _logger.LogInformation("Created Admin role.");
            }

            // Create user (Identity handles its own transaction)
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FirstName = firstName,
                LastName = lastName,
                Role = UserRole.Admin,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var createUserResult = await _userManager.CreateAsync(user, password);
            if (!createUserResult.Succeeded)
            {
                var errors = string.Join(", ", createUserResult.Errors.Select(e => e.Description));
                _logger.LogError("Failed to create user: {Errors}", errors);
                throw new ApplicationException($"Failed to create user: {errors}");
            }

            _logger.LogInformation("Created user: {UserId}", user.Id);

            // Assign Admin role to user (Identity handles its own transaction)
            var roleAssignResult = await _userManager.AddToRoleAsync(user, "Admin");
            if (!roleAssignResult.Succeeded)
            {
                var errors = string.Join(", ", roleAssignResult.Errors.Select(e => e.Description));
                _logger.LogError("Failed to assign Admin role to user: {Errors}", errors);
                // Cleanup: delete the user we just created
                await _userManager.DeleteAsync(user);
                throw new ApplicationException($"Failed to assign Admin role: {errors}");
            }

            _logger.LogInformation("Assigned Admin role to user: {UserId}", user.Id);

            // Create branch (this triggers auto-creation of vault via SaveChangesAsync override)
            var branch = new Branch
            {
                Name = branchName,
                CreatedAt = DateTimeOffset.UtcNow
            };

            _context.Branches.Add(branch);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Created branch: {BranchId}, name: {BranchName}", branch.Id, branchName);

            // Update user with branch ID (Identity handles its own transaction)
            user.BranchId = branch.Id;
            var updateUserResult = await _userManager.UpdateAsync(user);
            if (!updateUserResult.Succeeded)
            {
                var errors = string.Join(", ", updateUserResult.Errors.Select(e => e.Description));
                _logger.LogError("Failed to update user with branch ID: {Errors}", errors);
                // Cleanup on failure
                await CleanupDatabaseAsync();
                throw new ApplicationException($"Failed to update user: {errors}");
            }

            _logger.LogInformation("Updated user {UserId} with BranchId: {BranchId}", user.Id, branch.Id);

            // Update SetupComplete flag
            var setupConfig = await _context.AppConfigs
                .FirstOrDefaultAsync(c => c.Key == "SetupComplete");

            if (setupConfig != null)
            {
                setupConfig.Value = "true";
                _context.AppConfigs.Update(setupConfig);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Set SetupComplete to true.");
            }

            // Sign in the user so they can continue
            await _signInManager.SignInAsync(user, isPersistent: true);
            _logger.LogInformation("Signed in initial admin user: {UserId}", user.Id);

            _logger.LogInformation("Initial setup completed successfully for user: {Email}", email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during initial setup. Performing cleanup...");
            try
            {
                await CleanupDatabaseAsync();
            }
            catch (Exception cleanupEx)
            {
                _logger.LogError(cleanupEx, "Cleanup also failed.");
            }
            throw;
        }
    }

}
