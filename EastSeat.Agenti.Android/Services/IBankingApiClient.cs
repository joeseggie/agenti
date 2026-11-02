using EastSeat.Agenti.Web.Features.BankStatements;

namespace EastSeat.Agenti.Android.Services;

public interface IBankingApiClient
{
    Task<T> GetAsync<T>(string path, CancellationToken cancellationToken = default);
    Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest request, CancellationToken cancellationToken = default);
    Task<TResponse> PostAsync<TResponse>(string path, CancellationToken cancellationToken = default);
    Task<BankEvidenceInfoDto> UploadAsync(long accountId, Guid uploadKey, bool statement, string fileName,
        Stream content, CancellationToken cancellationToken = default);
    Task<BankingDownloadedFile> DownloadAsync(long evidenceId, CancellationToken cancellationToken = default);
}

public sealed record BankingDownloadedFile(string FileName, string ContentType, byte[] Content);

public sealed class BankingApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
