using System.Net.Http.Headers;

namespace EastSeat.Agenti.Android.Services;

public sealed class AuthenticatedApiHandler(IAccessTokenProvider tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.AbsolutePath.EndsWith("/api/auth/login", StringComparison.OrdinalIgnoreCase) == true)
            request.Headers.Authorization = null;
        else
        {
            var token = await tokens.GetAsync(cancellationToken);
            request.Headers.Authorization = string.IsNullOrWhiteSpace(token)
                ? null : new AuthenticationHeaderValue("Bearer", token);
        }
        return await base.SendAsync(request, cancellationToken);
    }
}
