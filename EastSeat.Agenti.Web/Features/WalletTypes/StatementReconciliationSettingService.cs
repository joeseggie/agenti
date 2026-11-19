using System.Data;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EastSeat.Agenti.Web.Features.WalletTypes;

public interface IStatementReconciliationSettingService
{
    /// <summary>Admin-only. Switches closing statement reconciliation on or off for a Bank wallet type.</summary>
    Task SetRequiredAsync(string userId, long walletTypeId, bool required, CancellationToken cancellationToken = default);
}

public sealed class StatementReconciliationSettingService(
    ApplicationDbContext db, IBankingAccessService access, IBusinessClock clock) : IStatementReconciliationSettingService
{
    public async Task SetRequiredAsync(
        string userId, long walletTypeId, bool required, CancellationToken cancellationToken = default)
    {
        (await access.GetActorAsync(userId, cancellationToken)).RequireAdmin();
        IDbContextTransaction? transaction = null;
        try
        {
            transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var walletType = await db.WalletTypes.SingleOrDefaultAsync(w => w.Id == walletTypeId, cancellationToken)
                ?? throw new BankingRuleException("Wallet type not found.");
            if (walletType.Type != WalletTypeEnum.Bank)
                throw new BankingRuleException("Statement reconciliation can only be switched for Bank wallet types.");
            if (walletType.RequiresStatementReconciliation == required)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }
            var now = clock.UtcNow;
            walletType.RequiresStatementReconciliation = required;
            walletType.UpdatedAt = now;
            db.WalletTypeReconciliationAudits.Add(new WalletTypeReconciliationAudit
            {
                WalletTypeId = walletType.Id, Required = required, ChangedByUserId = userId, ChangedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await BankingConcurrency.RollbackAfterConflictAsync(transaction);
            db.ChangeTracker.Clear();
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }
}
