using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace EastSeat.Agenti.Shared.Security;

public static class McpTokenSecret
{
    public const int DefaultLifetimeDays = 7;
    public const int MaxLifetimeDays = 30;
    private const string Prefix = "agenti_mcp_";

    public static string Create() => Prefix + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static bool TryHash(string? token, out string hash)
    {
        hash = string.Empty;
        if (token is null || token.Length != Prefix.Length + 43 ||
            !token.StartsWith(Prefix, StringComparison.Ordinal) ||
            token.AsSpan(Prefix.Length).ContainsAnyExcept(
                "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"))
            return false;

        hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        return true;
    }
}
