using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.CashSessions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EastSeat.Agenti.Web.Api;

/// <summary>
/// REST API controller for mobile cash sessions.
/// </summary>
[ApiController]
[Route("api/mobile/cash-sessions")]
[Authorize]
public class MobileCashSessionsController : ControllerBase
{
    private readonly ICashSessionService _cashSessionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public MobileCashSessionsController(
        ICashSessionService cashSessionService,
        UserManager<ApplicationUser> userManager)
    {
        _cashSessionService = cashSessionService;
        _userManager = userManager;
    }

    /// <summary>
    /// Gets all cash sessions.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetCashSessions()
    {
        var sessions = await _cashSessionService.GetCashSessionsAsync();
        return Ok(sessions);
    }

    /// <summary>
    /// Gets details for a specific cash session.
    /// </summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetCashSessionDetail(long id)
    {
        var detail = await _cashSessionService.GetCashSessionDetailAsync(id);
        if (detail == null)
        {
            return NotFound();
        }

        return Ok(detail);
    }
}
