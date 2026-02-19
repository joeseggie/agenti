using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.CashCounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EastSeat.Agenti.Web.Api;

/// <summary>
/// REST API controller for mobile cash counts.
/// </summary>
[ApiController]
[Route("api/mobile/cash-counts")]
[Authorize]
public class MobileCashCountsController : ControllerBase
{
    private readonly ICashCountService _cashCountService;
    private readonly UserManager<ApplicationUser> _userManager;

    public MobileCashCountsController(
        ICashCountService cashCountService,
        UserManager<ApplicationUser> userManager)
    {
        _cashCountService = cashCountService;
        _userManager = userManager;
    }

    /// <summary>
    /// Gets the current session status for the authenticated agent.
    /// </summary>
    [HttpGet("current-session")]
    public async Task<IActionResult> GetCurrentSession()
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var session = await _cashCountService.GetCurrentSessionAsync(userId);
        return Ok(session);
    }

    /// <summary>
    /// Gets a pre-populated cash count form for opening or closing count.
    /// </summary>
    [HttpGet("form")]
    public async Task<IActionResult> GetCashCountForm([FromQuery] bool isOpening = true)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var form = await _cashCountService.InitializeCashCountFormAsync(userId, isOpening);
        return Ok(form);
    }

    /// <summary>
    /// Submits a cash count (opening or closing).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SubmitCashCount([FromBody] CashCountFormModel form)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var result = await _cashCountService.SaveCashCountAsync(userId, form);
        if (!result.Success)
        {
            return BadRequest(result.ErrorMessage);
        }

        // Auto-submit after saving
        if (result.CashCountId.HasValue)
        {
            var submitResult = await _cashCountService.SubmitCashCountAsync(userId, result.CashCountId.Value);
            if (!submitResult.Success)
            {
                return BadRequest(submitResult.ErrorMessage);
            }

            return Ok(submitResult);
        }

        return Ok(result);
    }
}
