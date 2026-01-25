namespace EastSeat.Agenti.Web.Features.Setup;

/// <summary>
/// Service for handling initial system setup.
/// </summary>
public interface ISetupService
{
    /// <summary>
    /// Checks if initial setup has been completed.
    /// </summary>
    /// <returns>True if setup is complete, false otherwise.</returns>
    Task<bool> IsSetupCompleteAsync();

    /// <summary>
    /// Cleans up the database for a fresh setup start.
    /// Removes all user data, branches, vaults, and related entities.
    /// </summary>
    Task CleanupDatabaseAsync();

    /// <summary>
    /// Creates the initial admin user, branch, and vault.
    /// </summary>
    /// <param name="email">Email address for the admin user.</param>
    /// <param name="password">Password for the admin user.</param>
    /// <param name="firstName">First name of the admin user.</param>
    /// <param name="lastName">Last name of the admin user.</param>
    /// <param name="branchName">Name of the branch to create.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task CreateInitialAdminAndSetupAsync(string email, string password, string firstName, string lastName, string branchName);
}
