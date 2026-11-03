namespace EastSeat.Agenti.Shared.Domain.Entities;

public class BankEvidenceFile : IImmutableBankingRecord
{
    public long Id { get; set; }
    public long BankAccountId { get; set; }
    public Guid UploadKey { get; set; }
    public bool IsStatement { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
    public string UploadedByUserId { get; set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; set; }
    public BankAccount? BankAccount { get; set; }
}
