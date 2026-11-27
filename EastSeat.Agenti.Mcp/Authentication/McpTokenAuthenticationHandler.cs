using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace EastSeat.Agenti.Mcp.Authentication;

public sealed class McpTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, McpTokenValidator validator) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "McpToken";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Path.StartsWithSegments("/mcp"))
            return AuthenticateResult.NoResult();
        var headers = Request.Headers.Authorization;
        if (headers.Count != 1 || !AuthenticationHeaderValue.TryParse(headers[0], out var header) ||
            !header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(header.Parameter))
            return AuthenticateResult.Fail("An MCP bearer token is required.");

        var caller = await validator.ValidateAsync(header.Parameter, Context.RequestAborted);
        if (caller is null)
            return AuthenticateResult.Fail("Invalid MCP credential.");
        Context.Items[typeof(McpCaller)] = caller;
        var identity = new ClaimsIdentity(
            [new(ClaimTypes.NameIdentifier, caller.UserId), new(ClaimTypes.Role, caller.Role.ToString())], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer realm=\"agenti-mcp\"";
        return Task.CompletedTask;
    }
}
