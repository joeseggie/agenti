using System.Data;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.BankStatements;

public sealed class BankAccountService(
    ApplicationDbContext db, IBankingAccessService access, IBusinessClock clock,
    StatementProfileCatalog profiles) : IBankAccountService
{
    public async Task<List<BankAccountDto>> GetAccountsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetActorAsync(userId, cancellationToken);
        if (actor.Role is not (UserRole.Admin or UserRole.Supervisor))
            throw new UnauthorizedAccessException("Only administrators and supervisors may review bank accounts.");
        var accounts = await db.BankAccounts.AsNoTracking().Include(a => a.Wallet)
            .Where(a => a.BranchId == actor.BranchId).OrderBy(a => a.BankName).ToListAsync(cancellationToken);
        return accounts.Select(Map).ToList();
    }

    public async Task<BankAccountSetupDto> GetSetupAsync(string userId, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetActorAsync(userId, cancellationToken);
        actor.RequireAdmin();
        var wallets = await db.Wallets.AsNoTracking()
            .Where(w => w.Agent!.BranchId == actor.BranchId && w.Agent.IsActive && w.IsActive &&
                        w.WalletType!.Type == WalletTypeEnum.Bank &&
                        !db.BankAccounts.Any(a => a.WalletId == w.Id))
            .OrderBy(w => w.Name)
            .Select(w => new BankWalletOptionDto(w.Id, w.Name, w.Currency,
                w.Agent!.User!.FirstName + " " + w.Agent.User.LastName)).ToListAsync(cancellationToken);
        return new BankAccountSetupDto(wallets, profiles.GetProfiles());
    }

    public async Task<BankAccountDto> CreateAsync(
        string userId, BankAccountForm form, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetActorAsync(userId, cancellationToken);
        actor.RequireAdmin();
        Validate(form);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            if (await db.BankAccounts.AnyAsync(a => a.WalletId == form.WalletId, cancellationToken))
                throw new BankingRuleException("This wallet already has a bank account binding.");
            var wallet = await db.Wallets.SingleOrDefaultAsync(w => w.Id == form.WalletId && w.IsActive &&
                w.Agent!.BranchId == actor.BranchId && w.Agent.IsActive && w.WalletType!.Type == WalletTypeEnum.Bank, cancellationToken)
                ?? throw new BankingRuleException("Select an active bank wallet in this branch.");
            var profile = ValidateProfile(form.ImportProfileKey, form.BalanceBasis);
            var account = new BankAccount
            {
                BranchId = actor.BranchId, WalletId = wallet.Id, BankName = form.BankName.Trim(),
                BankIdentifier = profile?.BankIdentifier == "*"
                    ? form.BankName.Trim().ToUpperInvariant()
                    : profile?.BankIdentifier,
                AccountIdentifier = form.AccountIdentifier.Trim(), Currency = wallet.Currency,
                TimeZoneId = form.TimeZoneId, BalanceBasis = form.BalanceBasis,
                ImportProfileKey = profile?.Key, ImportProfileVerified = profile?.IsVerified == true,
                CreatedAt = clock.UtcNow
            };
            db.BankAccounts.Add(account);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            account.Wallet = wallet;
            return Map(account);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<BankAccountDto> ConfigureAsync(
        string userId, long accountId, BankAccountForm form, int expectedVersion, CancellationToken cancellationToken = default)
    {
        await access.RequireAccountAsync(userId, accountId, adminOnly: true, cancellationToken);
        Validate(form);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var account = await db.BankAccounts.Include(a => a.Wallet).SingleAsync(a => a.Id == accountId, cancellationToken);
            await db.Entry(account).ReloadAsync(cancellationToken);
            if (account.Version != expectedVersion)
                throw new BankingConflictException("The account configuration changed. Refresh and retry.");
            if (account.WalletId != form.WalletId)
                throw new BankingRuleException("A bank account's wallet binding cannot be reassigned.");
            var hasHistory = await db.CustomerTransactions.AnyAsync(t => t.BankAccountId == accountId, cancellationToken) ||
                await db.BankStatementImports.AnyAsync(i => i.BankAccountId == accountId && i.Status == StatementImportStatus.Committed, cancellationToken);
            if ((hasHistory || account.WorkflowEnabled) &&
                (account.AccountIdentifier != form.AccountIdentifier.Trim() || account.TimeZoneId != form.TimeZoneId ||
                 account.BalanceBasis != form.BalanceBasis))
                throw new BankingRuleException("Account identity, time zone and balance basis cannot be rewritten after financial activity.");
            var profile = ValidateProfile(form.ImportProfileKey, form.BalanceBasis);
            if ((hasHistory || account.WorkflowEnabled) && profile != null &&
                profile.BankIdentifier != "*" && account.BankIdentifier != null && account.BankIdentifier != profile.BankIdentifier)
                throw new BankingRuleException("A statement profile cannot change this account's bank identity.");
            if (account.WorkflowEnabled && profile?.IsVerified != true)
                throw new BankingRuleException("Pause enrollment before removing a verified statement profile.");
            account.BankName = form.BankName.Trim();
            account.AccountIdentifier = form.AccountIdentifier.Trim();
            account.TimeZoneId = form.TimeZoneId;
            account.BalanceBasis = form.BalanceBasis;
            account.ImportProfileKey = profile?.Key;
            if (profile?.BankIdentifier != "*")
                account.BankIdentifier = profile?.BankIdentifier ?? account.BankIdentifier;
            account.ImportProfileVerified = profile?.IsVerified == true;
            account.Version++;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Map(account);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<BankAccountDto> SetWorkflowAsync(
        string userId, long accountId, bool enabled, int expectedVersion, CancellationToken cancellationToken = default)
    {
        var scope = await access.RequireAccountAsync(userId, accountId, adminOnly: true, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var account = await db.BankAccounts.Include(a => a.Wallet).SingleAsync(a => a.Id == accountId, cancellationToken);
            await db.Entry(account).ReloadAsync(cancellationToken);
            if (account.Version != expectedVersion)
                throw new BankingConflictException("The account configuration changed. Refresh and retry.");
            if (enabled)
            {
                var profile = profiles.Find(account.ImportProfileKey) ??
                              profiles.Find(UniversalBankCsvStatementProfile.ProfileKey);
                if (!scope.IsActive || profile?.IsVerified != true ||
                    account.BalanceBasis == BankBalanceBasis.Unverified || profile.BalanceBasis != account.BalanceBasis)
                    throw new BankingRuleException("Enrollment requires the universal Agenti CSV parser and a confirmed matching balance basis.");
                if (await db.Agents.CountAsync(a => a.BranchId == scope.BranchId && a.IsActive, cancellationToken) != 1 ||
                    await db.BankAccounts.AnyAsync(a => a.BranchId == scope.BranchId && a.WorkflowEnabled && a.Id != accountId, cancellationToken))
                    throw new BankingRuleException("This release supports one active agent and one enrolled bank account per branch.");
                if (await db.Wallets.CountAsync(w => w.AgentId == scope.AgentId && w.IsActive &&
                        w.WalletType!.Type == WalletTypeEnum.Bank, cancellationToken) != 1 ||
                    await db.Wallets.CountAsync(w => w.AgentId == scope.AgentId && w.IsActive &&
                        w.WalletType!.Type == WalletTypeEnum.Cash, cancellationToken) != 1)
                    throw new BankingRuleException("Initial enrollment requires one active bank wallet and one active cash wallet.");
                if (await db.CashSessions.AnyAsync(s => s.BranchId == scope.BranchId &&
                    s.WorkflowVersion == SessionWorkflowVersion.Legacy && s.Status != CashSessionStatus.Closed, cancellationToken))
                    throw new BankingRuleException("Finish active legacy sessions before enabling the new workflow.");
                var currencies = await db.Wallets.Where(w => w.AgentId == scope.AgentId && w.IsActive)
                    .Select(w => w.Currency).Distinct().ToListAsync(cancellationToken);
                if (currencies.Count != 1 || currencies[0] != account.Currency)
                    throw new BankingRuleException("The enrolled agent's wallets must use the bank account currency.");
            }
            account.WorkflowEnabled = enabled;
            account.Version++;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Map(account);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private IStatementImportProfile? ValidateProfile(string? key, BankBalanceBasis basis)
    {
        var profile = profiles.Find(key) ?? profiles.Find(UniversalBankCsvStatementProfile.ProfileKey);
        if (profile == null)
            throw new BankingRuleException("The universal Agenti CSV statement parser is not installed.");
        if (basis != BankBalanceBasis.Unverified && profile.BalanceBasis != basis)
            throw new BankingRuleException("The statement profile and the entered bank balance use different bases.");
        return profile;
    }

    private static void Validate(BankAccountForm form)
    {
        if (string.IsNullOrWhiteSpace(form.BankName) || form.BankName.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(form.AccountIdentifier) || form.AccountIdentifier.Trim().Length > 256)
            throw new BankingRuleException("Enter a bank name and account identifier within the supported lengths.");
        if (!Enum.IsDefined(form.BalanceBasis))
            throw new BankingRuleException("Choose a supported balance basis.");
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(form.TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new BankingRuleException("Select a valid bank statement time zone.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new BankingRuleException("The selected time zone is invalid.");
        }
    }

    private static BankAccountDto Map(BankAccount account) => new(
        account.Id, account.WalletId, account.Wallet?.Name ?? string.Empty, account.BankName,
        account.AccountIdentifier, account.Currency, account.TimeZoneId, account.BalanceBasis,
        account.ImportProfileKey, true, account.WorkflowEnabled, account.IsActive, account.Version);
}
