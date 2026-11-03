namespace EastSeat.Agenti.Android.Services;

public sealed class SecureAccessTokenProvider : IAccessTokenProvider
{
    public const string TokenKey = "agenti_jwt_token";

    public Task<string?> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return SecureStorage.Default.GetAsync(TokenKey);
    }
}
