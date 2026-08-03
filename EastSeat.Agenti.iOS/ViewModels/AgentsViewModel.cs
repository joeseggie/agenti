using EastSeat.Agenti.iOS.Models;
using EastSeat.Agenti.iOS.Services;

namespace EastSeat.Agenti.iOS.ViewModels;

/// <summary>
/// ViewModel for the agents list page.
/// </summary>
public class AgentsViewModel : BaseViewModel
{
    private readonly IApiService _apiService;
    private List<AgentListItem> _agents = [];

    public List<AgentListItem> Agents
    {
        get => _agents;
        set => SetProperty(ref _agents, value);
    }

    public AgentsViewModel(IApiService apiService)
    {
        _apiService = apiService;
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var result = await _apiService.GetAgentsAsync();
            if (result?.Success == true)
                Agents = result.Data ?? [];
            else
                ErrorMessage = result?.Error ?? "Failed to load agents.";
        }
        catch (Exception)
        {
            ErrorMessage = "Unable to load agents. Check your connection.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
