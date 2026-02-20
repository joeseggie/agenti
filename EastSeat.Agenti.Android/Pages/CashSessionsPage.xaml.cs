using EastSeat.Agenti.Android.ViewModels;

namespace EastSeat.Agenti.Android.Pages;

public partial class CashSessionsPage : ContentPage
{
    private readonly CashSessionsViewModel _viewModel;

    public CashSessionsPage(CashSessionsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
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

        SessionsCollectionView.ItemsSource = _viewModel.Sessions;
    }

    private async void OnRefreshing(object sender, EventArgs e)
    {
        await LoadDataAsync();
        RefreshView.IsRefreshing = false;
    }
}
