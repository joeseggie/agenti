using EastSeat.Agenti.Web.Api;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Dashboard;
using EastSeat.Agenti.Shared.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Claims;

namespace EastSeat.Agenti.UnitTests.Api;

[Trait("Category", "Unit")]
[Trait("Feature", "MobileApi")]
public class MobileDashboardControllerTests
{
    private readonly Mock<IDashboardService> _dashboardServiceMock;
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly MobileDashboardController _sut;

    public MobileDashboardControllerTests()
    {
        _dashboardServiceMock = new Mock<IDashboardService>();

        var userStoreMock = new Mock<IUserStore<ApplicationUser>>();
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStoreMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _sut = new MobileDashboardController(
            _dashboardServiceMock.Object,
            _userManagerMock.Object);

        // Setup controller HTTP context with a user
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "user-1")], "TestAuth"))
            }
        };
    }

    [Fact]
    public async Task GetDashboard_WithNoAuthenticatedUser_ReturnsUnauthorized()
    {
        // Arrange
        _userManagerMock
            .Setup(u => u.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns((string?)null);

        // Act
        var result = await _sut.GetDashboard();

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task GetDashboard_WithAuthenticatedUser_ReturnsDashboardData()
    {
        // Arrange
        const string userId = "user-1";

        _userManagerMock
            .Setup(u => u.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(userId);

        var expectedDashboard = new DashboardViewModel
        {
            Wallets =
            [
                new WalletBalanceSummaryDto
                {
                    WalletId = 1,
                    WalletName = "Cash",
                    WalletTypeName = "Cash",
                    Balance = 1000m
                }
            ],
            SessionStatus = new SessionStatusDto
            {
                HasActiveSession = true,
                Status = CashSessionStatus.Open
            }
        };

        _dashboardServiceMock
            .Setup(s => s.GetDashboardAsync(userId))
            .ReturnsAsync(expectedDashboard);

        // Act
        var result = await _sut.GetDashboard();

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        okResult.Value.Should().BeOfType<DashboardViewModel>();
        var dashboard = (DashboardViewModel)okResult.Value!;
        dashboard.Wallets.Should().HaveCount(1);
        dashboard.TotalBalance.Should().Be(1000m);
    }
}
