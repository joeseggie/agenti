using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EastSeat.Agenti.Web.Api;

/// <summary>
/// REST API controller for mobile dashboard.
/// </summary>
[ApiController]
[Route("api/mobile/dashboard")]
[Authorize]
public class MobileDashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;
    private readonly UserManager<ApplicationUser> _userManager;

    public MobileDashboardController(
        IDashboardService dashboardService,
        UserManager<ApplicationUser> userManager)
    {
        _dashboardService = dashboardService;
        _userManager = userManager;
    }

    /// <summary>
    /// Gets the dashboard data for the currently authenticated agent.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetDashboard()
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var dashboard = await _dashboardService.GetDashboardAsync(userId);
        return Ok(dashboard);
    }
}
