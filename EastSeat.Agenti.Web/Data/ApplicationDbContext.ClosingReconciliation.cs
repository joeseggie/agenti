using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Data;

public partial class ApplicationDbContext
{
    public DbSet<DailyBankStatement> DailyBankStatements => Set<DailyBankStatement>();
    public DbSet<DailyBankStatementRow> DailyBankStatementRows => Set<DailyBankStatementRow>();
    public DbSet<ClosingReconciliationRun> ClosingReconciliationRuns => Set<ClosingReconciliationRun>();
    public DbSet<ClosingWalletResult> ClosingWalletResults => Set<ClosingWalletResult>();
    public DbSet<ClosingApprovalDecision> ClosingApprovalDecisions => Set<ClosingApprovalDecision>();

    private static void ConfigureClosingReconciliation(ModelBuilder builder)
    {
        builder.Entity<DailyBankStatement>(entity =>
        {
            entity.HasIndex(e => new { e.BankAccountId, e.StatementDate, e.Revision }).IsUnique();
            entity.HasIndex(e => e.SupersedesId).IsUnique().HasFilter("\"SupersedesId\" IS NOT NULL");
            entity.HasOne(e => e.BankAccount).WithMany().HasForeignKey(e => e.BankAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DailyBankStatement>().WithMany().HasForeignKey(e => e.SupersedesId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CashCount>().WithMany().HasForeignKey(e => e.OpeningCountId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.BalanceSource).HasConversion<string>().HasMaxLength(32)
                .HasDefaultValue(StatementBalanceSource.BankReported);
            entity.Property(e => e.UploadedByUserId).HasMaxLength(450);
            entity.Property(e => e.Currency).HasMaxLength(3);
            entity.Property(e => e.AccountIdentifier).HasMaxLength(256);
            entity.Property(e => e.FileName).HasMaxLength(256);
            entity.Property(e => e.ContentHash).HasMaxLength(64);
            entity.Property(e => e.SchemaHash).HasMaxLength(64);
            entity.Property(e => e.ReplacementReason).HasMaxLength(2000);
            entity.Property(e => e.OpeningBalance).HasPrecision(18, 2);
            entity.Property(e => e.ClosingBalance).HasPrecision(18, 2);
            entity.Property(e => e.TotalDebits).HasPrecision(18, 2);
            entity.Property(e => e.TotalCredits).HasPrecision(18, 2);
        });
        builder.Entity<DailyBankStatementRow>(entity =>
        {
            entity.HasIndex(e => new { e.StatementId, e.Sequence }).IsUnique();
            entity.HasOne<DailyBankStatement>().WithMany(e => e.Rows).HasForeignKey(e => e.StatementId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.Reference).HasMaxLength(256);
            entity.Property(e => e.Description).HasMaxLength(4000);
            entity.Property(e => e.Debit).HasPrecision(18, 2);
            entity.Property(e => e.Credit).HasPrecision(18, 2);
            entity.Property(e => e.RunningBalance).HasPrecision(18, 2);
        });
        builder.Entity<ClosingReconciliationRun>(entity =>
        {
            entity.HasIndex(e => e.ClosingCountId).IsUnique();
            entity.HasOne<CashCount>().WithMany().HasForeignKey(e => e.ClosingCountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CashCount>().WithMany().HasForeignKey(e => e.OpeningCountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.PerformedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.PerformedByUserId).HasMaxLength(450);
            entity.Property(e => e.InputHash).HasMaxLength(64);
        });
        builder.Entity<ClosingWalletResult>(entity =>
        {
            entity.HasIndex(e => new { e.RunId, e.WalletId }).IsUnique();
            entity.HasOne<ClosingReconciliationRun>().WithMany(e => e.Wallets).HasForeignKey(e => e.RunId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Wallet>().WithMany().HasForeignKey(e => e.WalletId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<BankAccount>().WithMany().HasForeignKey(e => e.BankAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DailyBankStatement>().WithMany().HasForeignKey(e => e.StatementId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.WalletName).HasMaxLength(256);
            entity.Property(e => e.BankName).HasMaxLength(200);
            entity.Property(e => e.AccountIdentifier).HasMaxLength(256);
            entity.Property(e => e.Currency).HasMaxLength(3);
            entity.Property(e => e.Explanation).HasMaxLength(2000);
            entity.Property(e => e.OpeningBalance).HasPrecision(18, 2);
            entity.Property(e => e.TotalDebits).HasPrecision(18, 2);
            entity.Property(e => e.TotalCredits).HasPrecision(18, 2);
            entity.Property(e => e.ExpectedClosing).HasPrecision(18, 2);
            entity.Property(e => e.SubmittedClosing).HasPrecision(18, 2);
            entity.Property(e => e.StatementOpening).HasPrecision(18, 2);
            entity.Property(e => e.StatementClosing).HasPrecision(18, 2);
            entity.Property(e => e.Difference).HasPrecision(18, 2);
        });
        builder.Entity<ClosingApprovalDecision>(entity =>
        {
            entity.HasIndex(e => e.ClosingCountId).IsUnique();
            entity.HasOne<CashCount>().WithMany().HasForeignKey(e => e.ClosingCountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ClosingReconciliationRun>().WithMany().HasForeignKey(e => e.RunId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.ApprovedByUserId).HasMaxLength(450);
            entity.Property(e => e.Reason).HasMaxLength(2000);
        });
    }
}
