using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Data;

public partial class ApplicationDbContext
{
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureBankingRecordsImmutable();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnsureBankingRecordsImmutable();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<BankEvidenceFile> BankEvidenceFiles => Set<BankEvidenceFile>();
    public DbSet<CustomerTransaction> CustomerTransactions => Set<CustomerTransaction>();
    public DbSet<CustomerMovement> CustomerMovements => Set<CustomerMovement>();
    public DbSet<CashCountSnapshot> CashCountSnapshots => Set<CashCountSnapshot>();
    public DbSet<SessionFloatSettlement> SessionFloatSettlements => Set<SessionFloatSettlement>();
    public DbSet<OpeningBankVariance> OpeningBankVariances => Set<OpeningBankVariance>();
    public DbSet<BankStatementImport> BankStatementImports => Set<BankStatementImport>();
    public DbSet<BankStatementRow> BankStatementRows => Set<BankStatementRow>();
    public DbSet<BankTransaction> BankTransactions => Set<BankTransaction>();
    public DbSet<BankStatementOccurrence> BankStatementOccurrences => Set<BankStatementOccurrence>();
    public DbSet<BankReconciliationReview> BankReconciliationReviews => Set<BankReconciliationReview>();
    public DbSet<BankReconciliationResult> BankReconciliationResults => Set<BankReconciliationResult>();
    public DbSet<BankReconciliationMatch> BankReconciliationMatches => Set<BankReconciliationMatch>();
    public DbSet<BankReconciliationException> BankReconciliationExceptions => Set<BankReconciliationException>();
    public DbSet<BankReconciliationDecision> BankReconciliationDecisions => Set<BankReconciliationDecision>();
    public DbSet<CashCountCorrectionRequest> CashCountCorrectionRequests => Set<CashCountCorrectionRequest>();

    private void EnsureBankingRecordsImmutable()
    {
        var modifiedRecord = ChangeTracker.Entries()
            .FirstOrDefault(e => e.Entity is IImmutableBankingRecord &&
                e.State is EntityState.Modified or EntityState.Deleted);
        if (modifiedRecord != null)
        {
            throw new InvalidOperationException(
                $"{modifiedRecord.Metadata.ClrType.Name} is immutable. Append a linked correction instead.");
        }
    }

    private static void ConfigureBankingModel(ModelBuilder builder)
    {
        builder.Entity<CashSession>(entity =>
        {
            entity.Property(e => e.WorkflowVersion).HasConversion<string>().HasMaxLength(40)
                .HasDefaultValue(SessionWorkflowVersion.Legacy);
            entity.Property(e => e.Version).IsConcurrencyToken();
        });
        builder.Entity<BankRun>()
            .HasIndex(e => new { e.AgentId, e.CommandId }).IsUnique()
            .HasFilter("\"CommandId\" IS NOT NULL");
        builder.Entity<BankRun>().HasIndex(e => e.AgentId);
        builder.Entity<BankRun>().Property(e => e.RequestHash).HasMaxLength(64);
        builder.Entity<WalletAdjustment>(entity =>
        {
            entity.Property(e => e.RequestHash).HasMaxLength(64);
            entity.Property(e => e.BankReference).HasMaxLength(256);
            entity.HasIndex(e => e.AgentId);
            entity.HasIndex(e => new { e.AgentId, e.CommandId }).IsUnique().HasFilter("\"CommandId\" IS NOT NULL");
        });
        builder.Entity<PendingTransaction>()
            .HasOne(e => e.CustomerTransaction).WithMany()
            .HasForeignKey(e => e.CustomerTransactionId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PendingTransaction>()
            .HasIndex(e => e.CustomerTransactionId).IsUnique()
            .HasFilter("\"CustomerTransactionId\" IS NOT NULL");

        builder.Entity<BankAccount>(entity =>
        {
            entity.Property(e => e.BankName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.BankIdentifier).HasMaxLength(100);
            entity.Property(e => e.AccountIdentifier).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Currency).HasMaxLength(3).IsRequired();
            entity.Property(e => e.TimeZoneId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.BalanceBasis).HasConversion<string>().HasMaxLength(40);
            entity.Property(e => e.ImportProfileKey).HasMaxLength(100);
            entity.Property(e => e.Version).IsConcurrencyToken();
            entity.HasIndex(e => e.WalletId).IsUnique();
            entity.HasOne(e => e.Wallet).WithMany().HasForeignKey(e => e.WalletId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Branch).WithMany().HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<BankEvidenceFile>(entity =>
        {
            entity.Property(e => e.FileName).HasMaxLength(256).IsRequired();
            entity.Property(e => e.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ContentHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.UploadedByUserId).HasMaxLength(450).IsRequired();
            entity.HasIndex(e => new { e.UploadedByUserId, e.UploadKey }).IsUnique();
            entity.HasIndex(e => new { e.BankAccountId, e.ContentHash });
            entity.HasOne(e => e.BankAccount).WithMany().HasForeignKey(e => e.BankAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<CustomerTransaction>(entity =>
        {
            entity.Property(e => e.Kind).HasConversion<string>().HasMaxLength(40);
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.Currency).HasMaxLength(3).IsRequired();
            entity.Property(e => e.CustomerIdentifier).HasMaxLength(256).IsRequired();
            entity.Property(e => e.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.RecordedByUserId).HasMaxLength(450).IsRequired();
            entity.HasIndex(e => new { e.AgentId, e.CommandId }).IsUnique();
            entity.HasIndex(e => new { e.OriginatingSessionId, e.BankAccountId });
            entity.HasOne(e => e.BankAccount).WithMany().HasForeignKey(e => e.BankAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Agent).WithMany().HasForeignKey(e => e.AgentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.OriginatingSession).WithMany().HasForeignKey(e => e.OriginatingSessionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CashWallet).WithMany().HasForeignKey(e => e.CashWalletId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<CustomerMovement>(entity =>
        {
            entity.Property(e => e.Kind).HasConversion<string>().HasMaxLength(40);
            entity.Property(e => e.TransactionKind).HasConversion<string>().HasMaxLength(40);
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.CustomerSettledAmount).HasPrecision(18, 2);
            entity.Property(e => e.CustomerIdentifier).HasMaxLength(256);
            entity.Property(e => e.CashEffect).HasPrecision(18, 2);
            entity.Property(e => e.BankEffect).HasPrecision(18, 2);
            entity.Property(e => e.BankReference).HasMaxLength(256);
            entity.Property(e => e.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(2000);
            entity.Property(e => e.SupportTicketNumber).HasMaxLength(100);
            entity.Property(e => e.RecordedByUserId).HasMaxLength(450).IsRequired();
            entity.HasIndex(e => new { e.RecordedByUserId, e.CommandId }).IsUnique();
            entity.HasIndex(e => new { e.CashSessionId, e.OccurredAt });
            entity.HasIndex(e => e.OccurrenceDate);
            entity.HasIndex(e => e.BankReference);
            entity.HasIndex(e => e.SupersedesMovementId).IsUnique()
                .HasFilter("\"SupersedesMovementId\" IS NOT NULL");
            entity.HasOne(e => e.CustomerTransaction).WithMany(e => e.Movements)
                .HasForeignKey(e => e.CustomerTransactionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CashSession).WithMany().HasForeignKey(e => e.CashSessionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.EvidenceFile).WithMany().HasForeignKey(e => e.EvidenceFileId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.RelatedMovement).WithMany().HasForeignKey(e => e.RelatedMovementId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.SupersedesMovement).WithMany().HasForeignKey(e => e.SupersedesMovementId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CorrectionRequest).WithMany().HasForeignKey(e => e.CorrectionRequestId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<CashCountSnapshot>(entity =>
        {
            entity.Property(e => e.TotalAmount).HasPrecision(18, 2);
            entity.Property(e => e.CapturedByUserId).HasMaxLength(450).IsRequired();
            entity.HasIndex(e => e.CashCountId).IsUnique();
            entity.HasOne(e => e.CashCount).WithMany().HasForeignKey(e => e.CashCountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.CapturedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<SessionFloatSettlement>(entity =>
        {
            entity.Property(e => e.Stage).HasConversion<string>().HasMaxLength(40);
            entity.Property(e => e.VaultAmount).HasPrecision(18, 2);
            entity.Property(e => e.ReportedAmount).HasPrecision(18, 2);
            entity.Property(e => e.BankVariance).HasPrecision(18, 2);
            entity.Property(e => e.PostedByUserId).HasMaxLength(450).IsRequired();
            entity.HasIndex(e => new { e.CashCountId, e.Stage }).IsUnique();
            entity.HasOne(e => e.Snapshot).WithMany().HasForeignKey(e => e.SnapshotId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CashCount).WithMany().HasForeignKey(e => e.CashCountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.VaultTransaction).WithMany().HasForeignKey(e => e.VaultTransactionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.PostedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<OpeningBankVariance>(entity =>
        {
            entity.Property(e => e.PreviousBankBalance).HasPrecision(18, 2);
            entity.Property(e => e.ObservedBankBalance).HasPrecision(18, 2);
            entity.HasIndex(e => new { e.BankAccountId, e.OpeningSnapshotId }).IsUnique();
            entity.HasOne(e => e.BankAccount).WithMany().HasForeignKey(e => e.BankAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.OpeningSnapshot).WithMany().HasForeignKey(e => e.OpeningSnapshotId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.PreviousClosingSnapshot).WithMany().HasForeignKey(e => e.PreviousClosingSnapshotId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.PreviousClosingCount).WithMany().HasForeignKey(e => e.PreviousClosingCountId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<BankStatementImport>(entity =>
        {
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(40);
            entity.Property(e => e.BalanceBasis).HasConversion<string>().HasMaxLength(40);
            entity.Property(e => e.ReportedAccountIdentifier).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Currency).HasMaxLength(3).IsRequired();
            entity.Property(e => e.CommitRequestHash).HasMaxLength(64);
            entity.Property(e => e.ProfileKey).HasMaxLength(100).IsRequired();
            entity.Property(e => e.SchemaHash).HasMaxLength(64);
            entity.Property(e => e.OpeningBaselineReason).HasMaxLength(1000);
            entity.Property(e => e.OpeningBalance).HasPrecision(18, 2);
            entity.Property(e => e.ClosingBalance).HasPrecision(18, 2);
            entity.Property(e => e.OpeningBaseline).HasPrecision(18, 2);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.Property(e => e.UploadedByUserId).HasMaxLength(450).IsRequired();
            entity.Property(e => e.Version).IsConcurrencyToken();
            entity.HasIndex(e => new { e.EvidenceFileId, e.Revision }).IsUnique();
            entity.HasIndex(e => new { e.BankAccountId, e.CoverageFromDate, e.CoverageToDate });
            entity.HasIndex(e => new { e.Status, e.CreatedAt });
            entity.HasOne(e => e.BankAccount).WithMany().HasForeignKey(e => e.BankAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.EvidenceFile).WithMany().HasForeignKey(e => e.EvidenceFileId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.PreviousImport).WithMany().HasForeignKey(e => e.PreviousImportId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<BankStatementRow>(entity =>
        {
            entity.Property(e => e.SourceLocation).HasMaxLength(256).IsRequired();
            entity.Property(e => e.TransactionNumber).HasMaxLength(256);
            entity.Property(e => e.Reference).HasMaxLength(256);
            entity.Property(e => e.CustomerIdentifier).HasMaxLength(256);
            entity.Property(e => e.Narration).HasMaxLength(4000);
            entity.Property(e => e.Currency).HasMaxLength(3).IsRequired();
            entity.Property(e => e.SignedAmount).HasPrecision(18, 2);
            entity.Property(e => e.RunningBalance).HasPrecision(18, 2);
            entity.Property(e => e.CorrectionReason).HasMaxLength(2000);
            entity.Property(e => e.CorrectedByUserId).HasMaxLength(450);
            entity.HasIndex(e => new { e.ImportId, e.Sequence, e.Revision }).IsUnique();
            entity.HasIndex(e => e.LedgerDate);
            entity.HasIndex(e => e.OccurrenceDate);
            entity.HasIndex(e => e.TransactionNumber);
            entity.HasIndex(e => e.Reference);
            entity.HasIndex(e => e.SupersedesRowId).IsUnique().HasFilter("\"SupersedesRowId\" IS NOT NULL");
            entity.HasOne(e => e.Import).WithMany(e => e.Rows).HasForeignKey(e => e.ImportId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.SupersedesRow).WithMany().HasForeignKey(e => e.SupersedesRowId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.CorrectedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<BankTransaction>(entity =>
        {
            entity.Property(e => e.ReliableIdentity).HasMaxLength(400);
            entity.Property(e => e.Fingerprint).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Classification).HasConversion<string>().HasMaxLength(60);
            entity.Property(e => e.ClassificationReason).HasMaxLength(2000);
            entity.Property(e => e.Version).IsConcurrencyToken();
            entity.HasIndex(e => new { e.BankAccountId, e.ReliableIdentity }).IsUnique()
                .HasFilter("\"ReliableIdentity\" IS NOT NULL");
            entity.HasIndex(e => e.AcceptedRowId).IsUnique();
            entity.HasIndex(e => new { e.BankAccountId, e.Fingerprint });
            entity.HasOne(e => e.BankAccount).WithMany().HasForeignKey(e => e.BankAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.AcceptedRow).WithMany().HasForeignKey(e => e.AcceptedRowId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.RelatedCustomerTransaction).WithMany()
                .HasForeignKey(e => e.RelatedCustomerTransactionId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<BankStatementOccurrence>(entity =>
        {
            entity.HasIndex(e => e.StatementRowId).IsUnique();
            entity.HasOne(e => e.BankTransaction).WithMany().HasForeignKey(e => e.BankTransactionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.StatementRow).WithMany().HasForeignKey(e => e.StatementRowId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<BankReconciliationReview>(entity =>
        {
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(40);
            entity.Property(e => e.Version).IsConcurrencyToken();
            entity.Property(e => e.CreatedByUserId).HasMaxLength(450).IsRequired();
            entity.HasIndex(e => new { e.BankAccountId, e.FromDate, e.ToDate });
            entity.HasOne(e => e.BankAccount).WithMany().HasForeignKey(e => e.BankAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<BankReconciliationResult>(entity =>
        {
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(40);
            entity.Property(e => e.BasisHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ReviewedByUserId).HasMaxLength(450).IsRequired();
            entity.HasIndex(e => new { e.CashSessionId, e.ReviewedAt });
            entity.HasIndex(e => new { e.ReviewedByUserId, e.CommandId }).IsUnique();
            entity.HasOne(e => e.Review).WithMany().HasForeignKey(e => e.ReviewId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CashSession).WithMany().HasForeignKey(e => e.CashSessionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Snapshot).WithMany().HasForeignKey(e => e.SnapshotId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.SupersedesResult).WithMany().HasForeignKey(e => e.SupersedesResultId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<BankReconciliationMatch>(entity =>
        {
            entity.ToTable(t => t.HasCheckConstraint("CK_BankReconciliationMatch_OneSource",
                "(CASE WHEN \"CustomerMovementId\" IS NULL THEN 0 ELSE 1 END + " +
                "CASE WHEN \"BankRunId\" IS NULL THEN 0 ELSE 1 END + " +
                "CASE WHEN \"WalletAdjustmentId\" IS NULL THEN 0 ELSE 1 END) = 1"));
            entity.Property(e => e.MatchedByUserId).HasMaxLength(450).IsRequired();
            entity.Property(e => e.Version).IsConcurrencyToken();
            entity.HasIndex(e => e.BankTransactionId).IsUnique().HasFilter("\"IsActive\" = TRUE");
            entity.HasIndex(e => e.CustomerMovementId).IsUnique()
                .HasFilter("\"IsActive\" = TRUE AND \"CustomerMovementId\" IS NOT NULL");
            entity.HasIndex(e => e.BankRunId).IsUnique()
                .HasFilter("\"IsActive\" = TRUE AND \"BankRunId\" IS NOT NULL");
            entity.HasIndex(e => e.WalletAdjustmentId).IsUnique()
                .HasFilter("\"IsActive\" = TRUE AND \"WalletAdjustmentId\" IS NOT NULL");
            entity.HasOne(e => e.BankTransaction).WithMany().HasForeignKey(e => e.BankTransactionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CustomerMovement).WithMany().HasForeignKey(e => e.CustomerMovementId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.BankRun).WithMany().HasForeignKey(e => e.BankRunId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.WalletAdjustment).WithMany().HasForeignKey(e => e.WalletAdjustmentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.MatchedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<BankReconciliationException>(entity =>
        {
            entity.Property(e => e.Kind).HasConversion<string>().HasMaxLength(40);
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.Explanation).HasMaxLength(2000).IsRequired();
            entity.Property(e => e.RecordedByUserId).HasMaxLength(450).IsRequired();
            entity.Property(e => e.Version).IsConcurrencyToken();
            entity.HasIndex(e => new { e.BankAccountId, e.CashSessionId, e.IsResolved });
            entity.HasOne(e => e.BankAccount).WithMany().HasForeignKey(e => e.BankAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CashSession).WithMany().HasForeignKey(e => e.CashSessionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CustomerTransaction).WithMany().HasForeignKey(e => e.CustomerTransactionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.PendingTransaction).WithMany().HasForeignKey(e => e.PendingTransactionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.BankTransaction).WithMany().HasForeignKey(e => e.BankTransactionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CustomerMovement).WithMany().HasForeignKey(e => e.CustomerMovementId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.BankRun).WithMany().HasForeignKey(e => e.BankRunId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.WalletAdjustment).WithMany().HasForeignKey(e => e.WalletAdjustmentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.EvidenceFile).WithMany().HasForeignKey(e => e.EvidenceFileId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<BankReconciliationDecision>(entity =>
        {
            entity.Property(e => e.Kind).HasConversion<string>().HasMaxLength(40);
            entity.Property(e => e.Reason).HasMaxLength(2000);
            entity.Property(e => e.PerformedByUserId).HasMaxLength(450).IsRequired();
            entity.HasIndex(e => new { e.BankAccountId, e.PerformedAt });
            entity.HasOne(e => e.BankAccount).WithMany().HasForeignKey(e => e.BankAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Review).WithMany().HasForeignKey(e => e.ReviewId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CashSession).WithMany().HasForeignKey(e => e.CashSessionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.BankTransaction).WithMany().HasForeignKey(e => e.BankTransactionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.PerformedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<CashCountCorrectionRequest>(entity =>
        {
            entity.Property(e => e.Reason).HasMaxLength(2000).IsRequired();
            entity.Property(e => e.RequestedByUserId).HasMaxLength(450).IsRequired();
            entity.Property(e => e.Version).IsConcurrencyToken();
            entity.HasIndex(e => e.CashCountId).IsUnique().HasFilter("\"IsOpen\" = TRUE");
            entity.HasOne(e => e.CashCount).WithMany().HasForeignKey(e => e.CashCountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Agent).WithMany().HasForeignKey(e => e.AgentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
