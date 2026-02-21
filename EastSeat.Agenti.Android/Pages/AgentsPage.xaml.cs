using EastSeat.Agenti.Android.Models;
using EastSeat.Agenti.Android.ViewModels;

namespace EastSeat.Agenti.Android.Pages;

public partial class AgentsPage : ContentPage
{
    private readonly AgentsViewModel _viewModel;
    private List<AgentListItem> _allAgents = [];

    public AgentsPage(AgentsViewModel viewModel)
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

        _allAgents = _viewModel.Agents;
        AgentsCollectionView.ItemsSource = _allAgents;
    }

    private async void OnRefreshing(object sender, EventArgs e)
    {
        await LoadDataAsync();
        RefreshView.IsRefreshing = false;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        var query = e.NewTextValue?.ToLowerInvariant() ?? string.Empty;
        AgentsCollectionView.ItemsSource = string.IsNullOrEmpty(query)
            ? _allAgents
            : _allAgents.Where(a =>
                a.FullName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.Code.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (a.Email?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
              .ToList();
    }

    private async void OnAgentSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not AgentListItem agent)
            return;

        AgentsCollectionView.SelectedItem = null;
        await DisplayAlertAsync(agent.FullName,
            $"Code: {agent.Code}\nWallets: {agent.WalletCount}\nBalance: UGX {agent.TotalBalance:N0}",
            "OK");
    }
}
