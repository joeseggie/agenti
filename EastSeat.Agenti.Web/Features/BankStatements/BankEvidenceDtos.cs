namespace EastSeat.Agenti.Web.Features.BankStatements;

public sealed record BankEvidenceInfoDto(
    long Id, long BankAccountId, string FileName, string ContentType, long Size,
    bool IsStatement, DateTimeOffset UploadedAt);

public sealed record BankEvidenceContentDto(BankEvidenceInfoDto Info, byte[] Content);

public sealed class BankEvidenceOptions
{
    public long MaxFileBytes { get; set; } = 5 * 1024 * 1024;
}
