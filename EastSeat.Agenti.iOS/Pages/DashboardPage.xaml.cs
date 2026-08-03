using EastSeat.Agenti.iOS.ViewModels;

namespace EastSeat.Agenti.iOS.Pages;

public partial class DashboardPage : ContentPage
{
    private readonly DashboardViewModel _viewModel;

    public DashboardPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel.Dashboard;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        LoadingIndicator.IsRunning = true;
        LoadingIndicator.IsVisible = true;

        await _viewModel.LoadAsync();

        LoadingIndicator.IsRunning = false;
        LoadingIndicator.IsVisible = false;

        if (_viewModel.HasError)
        {
            ErrorLabel.Text = _viewModel.ErrorMessage;
            ErrorLabel.IsVisible = true;
            return;
        }

        ErrorLabel.IsVisible = false;
        WelcomeLabel.Text = _viewModel.WelcomeMessage;

        var dashboard = _viewModel.Dashboard;
        if (dashboard is not null)
        {
            TotalBalanceLabel.Text = $"{dashboard.Currency} {dashboard.TotalBalance:N0}";
            SessionStatusLabel.Text = dashboard.SessionStatus.StatusDisplay;
            WalletsCollectionView.ItemsSource = dashboard.Wallets;
        }
    }

    private async void OnRefreshing(object sender, EventArgs e)
    {
        await LoadDataAsync();
        RefreshView.IsRefreshing = false;
    }

    private async void OnOpenSessionClicked(object sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//cashcount?isOpening=true");

    private async void OnCloseSessionClicked(object sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//cashcount?isOpening=false");
}
