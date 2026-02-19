using EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;
using EastSeat.Agenti.Web.Api;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace EastSeat.Agenti.UnitTests.Api;

[Trait("Category", "Unit")]
[Trait("Feature", "MobileApi")]
public class MobileAuthControllerTests
{
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly Mock<SignInManager<ApplicationUser>> _signInManagerMock;
    private readonly MobileAuthController _sut;

    public MobileAuthControllerTests()
    {
        var userStoreMock = new Mock<IUserStore<ApplicationUser>>();
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStoreMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var contextAccessorMock = new Mock<IHttpContextAccessor>();
        var claimsFactoryMock = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
        var optionsMock = new Mock<IOptions<IdentityOptions>>();
        optionsMock.Setup(o => o.Value).Returns(new IdentityOptions());
        var loggerMock = new Mock<ILogger<SignInManager<ApplicationUser>>>();

        _signInManagerMock = new Mock<SignInManager<ApplicationUser>>(
            _userManagerMock.Object,
            contextAccessorMock.Object,
            claimsFactoryMock.Object,
            optionsMock.Object,
            loggerMock.Object,
            null!,
            null!);

        _sut = new MobileAuthController(_signInManagerMock.Object, _userManagerMock.Object);
    }

    [Fact]
    public async Task Login_WithEmptyEmail_ReturnsBadRequest()
    {
        // Arrange
        var request = new MobileLoginRequest { Email = string.Empty, Password = "password" };

        // Act
        var result = await _sut.Login(request);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Login_WithEmptyPassword_ReturnsBadRequest()
    {
        // Arrange
        var request = new MobileLoginRequest { Email = "user@test.com", Password = string.Empty };

        // Act
        var result = await _sut.Login(request);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Login_WithNonExistentUser_ReturnsUnauthorized()
    {
        // Arrange
        var request = new MobileLoginRequest { Email = "nobody@test.com", Password = "password" };

        _userManagerMock
            .Setup(u => u.FindByEmailAsync(request.Email))
            .ReturnsAsync((ApplicationUser?)null);

        // Act
        var result = await _sut.Login(request);

        // Assert
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Login_WithInactiveUser_ReturnsUnauthorized()
    {
        // Arrange
        var inactiveUser = UserBuilder.Default().WithEmail("inactive@test.com").IsInactive().Build();
        var request = new MobileLoginRequest { Email = inactiveUser.Email!, Password = "password" };

        _userManagerMock
            .Setup(u => u.FindByEmailAsync(request.Email))
            .ReturnsAsync(inactiveUser);

        // Act
        var result = await _sut.Login(request);

        // Assert
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Login_WithInvalidPassword_ReturnsUnauthorized()
    {
        // Arrange
        var user = UserBuilder.Default().WithEmail("user@test.com").Build();
        var request = new MobileLoginRequest { Email = user.Email!, Password = "wrongpassword" };

        _userManagerMock
            .Setup(u => u.FindByEmailAsync(request.Email))
            .ReturnsAsync(user);

        _signInManagerMock
            .Setup(s => s.CheckPasswordSignInAsync(user, request.Password, true))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Failed);

        // Act
        var result = await _sut.Login(request);

        // Assert
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsOkWithLoginResponse()
    {
        // Arrange
        var user = UserBuilder.Default()
            .WithEmail("agent@test.com")
            .WithFirstName("John")
            .WithLastName("Doe")
            .Build();
        var request = new MobileLoginRequest { Email = user.Email!, Password = "correct_password" };

        _userManagerMock
            .Setup(u => u.FindByEmailAsync(request.Email))
            .ReturnsAsync(user);

        _signInManagerMock
            .Setup(s => s.CheckPasswordSignInAsync(user, request.Password, true))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

        _signInManagerMock
            .Setup(s => s.SignInAsync(user, false, null))
            .Returns(Task.CompletedTask);

        _userManagerMock
            .Setup(u => u.GetRolesAsync(user))
            .ReturnsAsync(["Agent"]);

        // Act
        var result = await _sut.Login(request);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        okResult.Value.Should().BeOfType<MobileLoginResponse>();
        var response = (MobileLoginResponse)okResult.Value!;
        response.Success.Should().BeTrue();
        response.FullName.Should().Be("John Doe");
        response.Email.Should().Be(user.Email);
        response.Role.Should().Be("Agent");
    }
}
