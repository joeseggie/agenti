using System.Globalization;
using EastSeat.Agenti.Android.Models;
using EastSeat.Agenti.Android.Services;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.Web.Features.BankReconciliation;
using Microsoft.Extensions.Logging;

namespace EastSeat.Agenti.Android.Pages;

public abstract class BankingPageBase : ContentPage
{
    protected readonly IBankingApiClient Api;
    protected readonly IAuthService Auth;
    protected readonly IBankingDraftStore Drafts;
    protected readonly VerticalStackLayout Body = new() { Spacing = 12 };
    private readonly ILogger _logger;
    private readonly Label _error = new() { TextColor = Colors.Firebrick, IsVisible = false };
    private readonly ActivityIndicator _loading = new() { Color = Color.FromArgb("#1976D2"), IsVisible = false };
    private readonly Button _retry = new() { Text = "Retry loading", IsVisible = false };
    private readonly List<Action> _formReaders = [];
    private readonly SemaphoreSlim _draftWrites = new(1, 1);
    private readonly List<string> _previews = [];
    private Func<Task>? _saveDraft;
    private bool _busy;

    protected BankingPageBase(IBankingApiClient api, IAuthService auth, IBankingDraftStore drafts, ILogger logger)
    {
        Api = api;
        Auth = auth;
        Drafts = drafts;
        _logger = logger;
        _retry.Clicked += async (_, _) => await LoadInitialAsync();
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 16, Spacing = 12, Children = { _loading, _error, _retry, Body }
            }
        };
    }

    protected string UserId => Auth.CurrentUser?.UserId ?? throw new BankingRuleException("Sign in before opening banking.");
    protected bool IsAdmin => Auth.CurrentUser?.Role == "Admin";
    protected bool CanApproveOpening => Auth.CurrentUser?.Role is "Admin" or "Supervisor";

    protected abstract Task LoadAsync();

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (Body.Children.Count == 0)
            await LoadInitialAsync();
    }

    private Task LoadInitialAsync() => RunAsync(async () =>
    {
        var capabilities = await Api.GetAsync<BankingCapabilitiesDto>("capabilities");
        if (capabilities.ContractVersion != 1)
            throw new BankingRuleException("The server and Android banking versions do not match. Install the coordinated client/backend release.");
        await LoadAsync();
    });

    protected override void OnDisappearing()
    {
        foreach (var path in _previews)
        {
            try { File.Delete(path); }
            catch (IOException) { _logger.LogWarning("A private banking preview could not be removed."); }
            catch (UnauthorizedAccessException) { _logger.LogWarning("A private banking preview could not be removed."); }
        }
        _previews.Clear();
        base.OnDisappearing();
    }

    protected async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        _loading.IsVisible = _loading.IsRunning = true;
        Body.IsEnabled = false;
        _error.IsVisible = false;
        _retry.IsVisible = false;
        try { await action(); }
        catch (BankingApiException exception) { ShowError(exception.Message); }
        catch (BankingRuleException exception) { ShowError(exception.Message); }
        catch (HttpRequestException) { ShowError("Connection failed. Your draft is retained. Refresh status before retrying the same request."); }
        catch (OperationCanceledException) { ShowError("The request was not confirmed. Your draft is retained. Refresh status before retrying."); }
        catch (Exception exception)
        {
            _logger.LogError("Banking screen failed with {ExceptionType}.", exception.GetType().Name);
            ShowError("Banking could not complete this action. Your draft is retained; refresh or sign in again.");
        }
        finally
        {
            Body.IsEnabled = true;
            _loading.IsVisible = _loading.IsRunning = false;
            _busy = false;
        }
    }

    protected void Reset(string heading)
    {
        Body.Children.Clear();
        _formReaders.Clear();
        _saveDraft = null;
        Body.Children.Add(new Label { Text = heading, FontSize = 21, FontAttributes = FontAttributes.Bold, AutomationId = "banking-heading" });
    }

    protected void Text(string text) => Body.Children.Add(new Label { Text = text });

    protected Button Action(string text, Func<Task> action)
    {
        var button = new Button { Text = text, CornerRadius = 8, BackgroundColor = Color.FromArgb("#1976D2"), TextColor = Colors.White };
        button.Clicked += async (_, _) => await RunAsync(action);
        Body.Children.Add(button);
        return button;
    }

    protected async Task<BankingDraft<T>> EditAsync<T>(string name, Func<T> create) where T : class, new()
    {
        var draft = await Drafts.LoadAsync<BankingDraft<T>>(UserId, name) ?? new BankingDraft<T> { Form = create() };
        var owner = UserId;
        _saveDraft = async () =>
        {
            await _draftWrites.WaitAsync();
            try { await Drafts.SaveAsync(owner, name, draft); }
            finally { _draftWrites.Release(); }
        };
        await _saveDraft();
        return draft;
    }

    protected Entry Field<T>(BankingDraft<T> draft, string key, string label, string value, Action<string> apply, bool numeric = false)
        where T : class, new()
    {
        Text(label);
        var input = new Entry { Text = draft.Text.GetValueOrDefault(key, value), Keyboard = numeric ? Keyboard.Numeric : Keyboard.Default,
            AutomationId = "banking-" + key };
        var save = _saveDraft;
        input.TextChanged += async (_, args) =>
        {
            draft.Text[key] = args.NewTextValue ?? string.Empty;
            await SaveEditAsync(save);
        };
        _formReaders.Add(() => apply(input.Text ?? string.Empty));
        Body.Children.Add(input);
        return input;
    }

    protected void Toggle(string label, bool value, Action<bool> apply)
    {
        Text(label);
        var control = new Switch { IsToggled = value };
        var save = _saveDraft;
        control.Toggled += async (_, args) => { apply(args.Value); await SaveEditAsync(save); };
        Body.Children.Add(control);
    }

    protected Picker Select<T>(string label, IReadOnlyList<T> items, Func<T, string> display, Func<T, bool> selected, Action<T> apply)
    {
        Text(label);
        var picker = new Picker { Title = label };
        foreach (var item in items) picker.Items.Add(display(item));
        picker.SelectedIndex = items.ToList().FindIndex(item => selected(item));
        var save = _saveDraft;
        picker.SelectedIndexChanged += async (_, _) =>
        {
            if (picker.SelectedIndex < 0) return;
            apply(items[picker.SelectedIndex]);
            await SaveEditAsync(save);
        };
        Body.Children.Add(picker);
        return picker;
    }

    protected void SelectEnum<T>(string label, T value, Action<T> apply) where T : struct, Enum =>
        Select(label, Enum.GetValues<T>(), item => item.ToString(), item => item.Equals(value), apply);

    protected async Task ReadFormAsync()
    {
        foreach (var read in _formReaders) read();
        if (_saveDraft != null) await _saveDraft();
    }

    protected async Task SaveDraftAsync()
    {
        if (_saveDraft != null) await _saveDraft();
    }

    private async Task SaveEditAsync(Func<Task>? save)
    {
        if (save == null) return;
        try { await save(); }
        catch (Exception exception)
        {
            _logger.LogError("Secure banking draft storage failed with {ExceptionType}.", exception.GetType().Name);
            ShowError("The draft could not be saved securely on this device. Keep this screen open until it is submitted.");
        }
    }

    protected void Evidence<T>(BankingDraft<T> draft, Func<long> account, bool statement, Action<long> uploaded)
        where T : class, new()
    {
        FileResult? file = null;
        var selected = new Label { Text = "No new file selected." };
        Body.Children.Add(selected);
        Action(statement ? "Choose statement file" : "Choose receipt evidence", async () =>
        {
            var picked = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = statement ? "Bank statement" : "Receipt evidence" });
            if (picked == null) return;
            file = picked;
            draft.UploadKey = Guid.NewGuid();
            selected.Text = file.FileName;
            await SaveDraftAsync();
        });
        Action("Upload selected file (or retry)", async () =>
        {
            if (file == null) throw new BankingRuleException("Choose a file first.");
            await SaveDraftAsync();
            await using var stream = await file.OpenReadAsync();
            var result = await Api.UploadAsync(account(), draft.UploadKey, statement, file.FileName, stream);
            uploaded(result.Id);
            selected.Text = $"Uploaded: {result.FileName} (evidence {result.Id})";
            await SaveDraftAsync();
        });
    }

    protected void Original(long evidenceId) => Action($"Open protected evidence {evidenceId}", async () =>
    {
        if (!await DisplayAlertAsync("Private evidence", "Open this file using a viewer on this device? Do not share it outside your authorized banking work.", "Open", "Cancel"))
            return;
        var file = await Api.DownloadAsync(evidenceId);
        var path = Path.Combine(FileSystem.CacheDirectory, $"banking-preview-{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}");
        await File.WriteAllBytesAsync(path, file.Content);
        _previews.Add(path);
        if (!await Launcher.Default.OpenAsync(new OpenFileRequest(file.FileName, new ReadOnlyFile(path, file.ContentType))))
            throw new BankingRuleException("No installed viewer can open this file.");
    });

    protected void ShowError(string message) { _error.Text = message; _error.IsVisible = true; _retry.IsVisible = Body.Children.Count == 0; }
    protected static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    protected static decimal Money(string value) => decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
        CultureInfo.InvariantCulture, out var amount) ? amount : throw new BankingRuleException("Enter a valid amount using a decimal point.");
    protected static long ParseId(string value) => long.TryParse(value, out var id) && id > 0 ? id : throw new BankingRuleException("Choose a valid record identity.");
    protected static long? OptionalId(string value) => string.IsNullOrWhiteSpace(value) ? null : ParseId(value);
    protected static DateOnly Date(string value) => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out var date) ? date : throw new BankingRuleException("Enter a date as yyyy-MM-dd.");
    protected static string Time(DateTimeOffset value) => value == default ? string.Empty : BusinessTimeText.Format(value);
}
