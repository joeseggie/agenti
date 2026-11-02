using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EastSeat.Agenti.Android.Models;
using EastSeat.Agenti.Web.Features.BankStatements;

namespace EastSeat.Agenti.Android.Services;

public sealed class BankingApiClient(HttpClient client) : IBankingApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync(Route(path), cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    public async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await client.PostAsJsonAsync(Route(path), request, Json, cancellationToken);
        return await ReadAsync<TResponse>(response, cancellationToken);
    }

    public async Task<TResponse> PostAsync<TResponse>(string path, CancellationToken cancellationToken = default)
    {
        using var response = await client.PostAsync(Route(path), new StringContent(string.Empty, Encoding.UTF8, "application/json"), cancellationToken);
        return await ReadAsync<TResponse>(response, cancellationToken);
    }

    public async Task<BankEvidenceInfoDto> UploadAsync(long accountId, Guid uploadKey, bool statement, string fileName,
        Stream content, CancellationToken cancellationToken = default)
    {
        using var body = new MultipartFormDataContent();
        body.Add(new StringContent(accountId.ToString(System.Globalization.CultureInfo.InvariantCulture)), "bankAccountId");
        body.Add(new StringContent(uploadKey.ToString()), "uploadKey");
        body.Add(new StringContent(statement.ToString()), "isStatement");
        body.Add(new StreamContent(content), "file", Path.GetFileName(fileName));
        using var response = await client.PostAsync(Route("evidence"), body, cancellationToken);
        return await ReadAsync<BankEvidenceInfoDto>(response, cancellationToken);
    }

    public async Task<BankingDownloadedFile> DownloadAsync(long evidenceId, CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync(Route($"evidence/{evidenceId}"), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            await ReadAsync<object>(response, cancellationToken);
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar ??
                       response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? $"evidence-{evidenceId}";
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return new BankingDownloadedFile(Path.GetFileName(fileName.Replace('\\', '/')),
            response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream", bytes);
    }

    private static string Route(string path)
    {
        if (path.StartsWith('/') || path.Contains("://", StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Banking API paths must remain relative to the configured backend.", nameof(path));
        return $"api/banking/{path}";
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        if (status == 401)
            throw new BankingApiException(status, "Your session expired. Sign in again; your draft is retained.");
        if (status == 403)
            throw new BankingApiException(status, "You do not have permission for this banking operation.");
        ApiResponse<T>? result;
        try
        {
            result = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(Json, cancellationToken);
        }
        catch (JsonException)
        {
            throw new BankingApiException(status, "The server returned an unreadable response. Refresh the operation's status before retrying.");
        }
        if (!response.IsSuccessStatusCode || result?.Success != true || result.Data is null)
            throw new BankingApiException(status, result?.Error ?? "The banking request was not confirmed. Your draft is retained.");
        return result.Data;
    }
}
