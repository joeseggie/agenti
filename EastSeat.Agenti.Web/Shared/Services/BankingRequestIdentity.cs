using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EastSeat.Agenti.Shared.Services;

public static class BankingRequestIdentity
{
    public static string Hash<T>(T payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));
}
