using EastSeat.Agenti.Android.ViewModels;

namespace EastSeat.Agenti.Android.Pages;

[QueryProperty(nameof(IsOpeningParam), "isOpening")]
public partial class CashCountPage : ContentPage
{
    private readonly CashCountViewModel _viewModel;
    private string? _isOpeningParam;

    public string? IsOpeningParam
    {
        get => _isOpeningParam;
        set
        {
            _isOpeningParam = value;
            if (bool.TryParse(value, out var isOpening))
                _ = InitializeFormAsync(isOpening);
        }
    }

    public CashCountPage(CashCountViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadSessionStatusAsync();
    }

    private async Task LoadSessionStatusAsync()
    {
        SetLoading(true);
        await _viewModel.LoadCurrentSessionAsync();
        SetLoading(false);

        if (_viewModel.HasError)
        {
            ShowError(_viewModel.ErrorMessage);
            return;
        }

        var session = _viewModel.CurrentSession;
        if (session is not null)
        {
            SessionStatusLabel.Text = session.StatusText;
            OpeningCountButton.IsEnabled = session.CanPerformOpeningCount;
            ClosingCountButton.IsEnabled = session.CanPerformClosingCount;
        }
    }

    private async Task InitializeFormAsync(bool isOpening)
    {
        SetLoading(true);
        await _viewModel.InitializeFormAsync(isOpening);
        SetLoading(false);

        if (_viewModel.HasError)
        {
            ShowError(_viewModel.ErrorMessage);
            return;
        }

        var form = _viewModel.Form;
        if (form is not null)
        {
            CountTitleLabel.Text = isOpening ? "Opening Count" : "Closing Count";
            CountTitleLabel.IsVisible = true;
            WalletEntriesView.ItemsSource = form.WalletEntries;
            WalletEntriesView.IsVisible = true;
            SubmitButton.IsVisible = true;
        }
    }

    private async void OnOpeningCountClicked(object sender, EventArgs e) =>
        await InitializeFormAsync(true);

    private async void OnClosingCountClicked(object sender, EventArgs e) =>
        await InitializeFormAsync(false);

    private async void OnSubmitClicked(object sender, EventArgs e)
    {
        SetLoading(true);
        var success = await _viewModel.SaveAndSubmitAsync();
        SetLoading(false);

        if (success)
        {
            await DisplayAlertAsync("Success",
                "Cash count submitted successfully.",
                "OK");
            await Shell.Current.GoToAsync("//dashboard");
        }
        else
        {
            ShowError(_viewModel.ErrorMessage);
        }
    }

    private void SetLoading(bool loading)
    {
        LoadingIndicator.IsRunning = loading;
        LoadingIndicator.IsVisible = loading;
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }
}
