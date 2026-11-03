using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EastSeat.Agenti.Android.Services;

public interface IBankingDraftStore
{
    Task SaveAsync<T>(string userId, string name, T draft);
    Task<T?> LoadAsync<T>(string userId, string name);
    void Remove(string userId, string name);
}

public sealed class BankingDraftStore : IBankingDraftStore
{
    public Task SaveAsync<T>(string userId, string name, T draft) =>
        SecureStorage.Default.SetAsync(Key(userId, name), JsonSerializer.Serialize(draft));

    public async Task<T?> LoadAsync<T>(string userId, string name)
    {
        var value = await SecureStorage.Default.GetAsync(Key(userId, name));
        return value == null ? default : JsonSerializer.Deserialize<T>(value);
    }

    public void Remove(string userId, string name) => SecureStorage.Default.Remove(Key(userId, name));

    private static string Key(string userId, string name) =>
        "agenti_banking_draft_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}:{name}")));
}
