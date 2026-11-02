namespace EastSeat.Agenti.Android.Services;

public interface IAccessTokenProvider
{
    Task<string?> GetAsync(CancellationToken cancellationToken = default);
}
