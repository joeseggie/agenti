using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Shared.Domain.Entities;

public class BankStatementImport
{
    public long Id { get; set; }
    public long BankAccountId { get; set; }
    public long EvidenceFileId { get; set; }
    public StatementImportStatus Status { get; set; }
    public string ProfileKey { get; set; } = string.Empty;
    public int ProfileVersion { get; set; }
    public int Version { get; set; }
    public int Revision { get; set; }
    public long? PreviousImportId { get; set; }
    public DateTimeOffset? CoverageFrom { get; set; }
    public DateTimeOffset? CoverageTo { get; set; }
    public DateOnly? CoverageFromDate { get; set; }
    public DateOnly? CoverageToDate { get; set; }
    public decimal? OpeningBalance { get; set; }
    public decimal? ClosingBalance { get; set; }
    public bool CoverageVerified { get; set; }
    public bool CoverageDeclaredComplete { get; set; }
    public BankBalanceBasis BalanceBasis { get; set; }
    public string ReportedAccountIdentifier { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? CommitRequestHash { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public Guid? LeaseId { get; set; }
    public string UploadedByUserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CommittedAt { get; set; }
    public BankAccount? BankAccount { get; set; }
    public BankEvidenceFile? EvidenceFile { get; set; }
    public BankStatementImport? PreviousImport { get; set; }
    public ICollection<BankStatementRow> Rows { get; set; } = [];
}
