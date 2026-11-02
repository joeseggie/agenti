namespace EastSeat.Agenti.Android.Models;

public sealed class BankingDraft<T> where T : class, new()
{
    public T Form { get; set; } = new();
    public Dictionary<string, string> Text { get; set; } = [];
    public Guid UploadKey { get; set; } = Guid.NewGuid();
}
