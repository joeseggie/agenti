namespace EastSeat.Agenti.Web.Features.BankStatements;

public interface IBankEvidenceService
{
    Task<BankEvidenceInfoDto> UploadAsync(
        string userId, long bankAccountId, Guid uploadKey, string fileName, bool isStatement,
        Stream content, CancellationToken cancellationToken = default);
    Task<BankEvidenceContentDto> DownloadAsync(
        string userId, long evidenceId, CancellationToken cancellationToken = default);
}
