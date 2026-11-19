using EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Theme;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;
using Moq;
using System.Security.Claims;

namespace EastSeat.Agenti.UnitTests.Services;

/// <summary>
/// Unit tests for <see cref="ThemeService"/>.
/// </summary>
public class ThemeServiceTests : IDisposable
{
    private readonly DbContextOptions<ApplicationDbContext> _options;
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<AuthenticationStateProvider> _authStateProviderMock;
    private readonly ThemeService _themeService;

    public ThemeServiceTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ApplicationDbContext(_options);
        _authStateProviderMock = new Mock<AuthenticationStateProvider>();

        var dbFactoryMock = new Mock<IDbContextFactory<ApplicationDbContext>>();
        dbFactoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ApplicationDbContext(_options));

        _themeService = new ThemeService(dbFactoryMock.Object, _authStateProviderMock.Object);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    [Theory]
    [InlineData(null, "dark", true, 1)]
    [InlineData("light", "dark", false, 0)]
    [InlineData("dark", "light", true, 0)]
    public async Task UpdateSystemPreference_RespectsSavedPreference_WithoutReloadingUser(
        string? savedPreference, string systemPreference, bool expectedDarkMode, int expectedEvents)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var user = UserBuilder.Default().WithId("system-theme-user")
            .WithThemePreference(savedPreference).Build();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        _authStateProviderMock.Setup(provider => provider.GetAuthenticationStateAsync())
            .ReturnsAsync(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id)], "test"))));
        var factory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        factory.Setup(item => item.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ApplicationDbContext(options));
        var service = new ThemeService(factory.Object, _authStateProviderMock.Object);
        await service.InitializeAsync(null);
        var events = 0;
        service.ThemeChanged += (_, _) => events++;

        service.UpdateSystemPreference(systemPreference);
        service.UpdateSystemPreference(systemPreference);

        service.IsDarkMode.Should().Be(expectedDarkMode);
        events.Should().Be(expectedEvents);
        factory.Verify(item => item.CreateDbContextAsync(It.IsAny<CancellationToken>()), Times.Once);
        _authStateProviderMock.Verify(provider => provider.GetAuthenticationStateAsync(), Times.Once);
        (await service.GetUserPreferenceAsync()).Should()
            .Be(savedPreference ?? ThemePreferenceConstants.System);

        (await service.SetUserPreferenceAsync(ThemePreferenceConstants.System)).Should().BeTrue();
        service.IsDarkMode.Should().Be(systemPreference == ThemePreferenceConstants.Dark);
    }

    [Fact]
    public void UpdateSystemPreference_InvalidValue_RejectsInput()
    {
        var action = () => _themeService.UpdateSystemPreference("invalid");

        action.Should().Throw<ArgumentException>();
        _themeService.IsDarkMode.Should().BeFalse();
    }

    #region InitializeAsync Tests

    [Fact]
    public async Task InitializeAsync_WithAnonymousUser_UsesSystemPreference()
    {
        // Arrange
        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        var authState = new AuthenticationState(anonymousUser);
        _authStateProviderMock.Setup(x => x.GetAuthenticationStateAsync())
            .ReturnsAsync(authState);

        // Act
        await _themeService.InitializeAsync("dark");
        var effectiveTheme = await _themeService.GetEffectiveThemeAsync();

        // Assert
        effectiveTheme.Should().Be("dark");
    }

    [Fact]
    public async Task InitializeAsync_WithAnonymousUserAndNoSystemPreference_UsesLight()
    {
        // Arrange
        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        var authState = new AuthenticationState(anonymousUser);
        _authStateProviderMock.Setup(x => x.GetAuthenticationStateAsync())
            .ReturnsAsync(authState);

        // Act
        await _themeService.InitializeAsync(null);
        var effectiveTheme = await _themeService.GetEffectiveThemeAsync();

        // Assert
        effectiveTheme.Should().Be("light");
    }

    [Fact]
    public async Task InitializeAsync_WithAuthenticatedUserButNoUserId_UsesSystemPreference()
    {
        // Arrange
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "test@test.com") }, "test");
        var authenticatedUser = new ClaimsPrincipal(identity);
        var authState = new AuthenticationState(authenticatedUser);
        _authStateProviderMock.Setup(x => x.GetAuthenticationStateAsync())
            .ReturnsAsync(authState);

        // Act
        await _themeService.InitializeAsync("dark");
        var effectiveTheme = await _themeService.GetEffectiveThemeAsync();

        // Assert
        effectiveTheme.Should().Be("dark");
    }

    [Fact]
    public async Task InitializeAsync_WithAuthenticatedUserNotInDatabase_UsesSystemPreference()
    {
        // Arrange
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "non-existent-user-id")
        }, "test");
        var authenticatedUser = new ClaimsPrincipal(identity);
        var authState = new AuthenticationState(authenticatedUser);
        _authStateProviderMock.Setup(x => x.GetAuthenticationStateAsync())
            .ReturnsAsync(authState);

        // Act
        await _themeService.InitializeAsync("dark");
        var effectiveTheme = await _themeService.GetEffectiveThemeAsync();

        // Assert
        effectiveTheme.Should().Be("dark");
    }

    [Fact]
    public async Task InitializeAsync_WithUserPreferringLight_UsesLight()
    {
        // Arrange
        var user = UserBuilder.Default()
            .WithId("user-123")
            .WithThemePreference(ThemePreferenceConstants.Light)
            .Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-123")
        }, "test");
        var authenticatedUser = new ClaimsPrincipal(identity);
        var authState = new AuthenticationState(authenticatedUser);
        _authStateProviderMock.Setup(x => x.GetAuthenticationStateAsync())
            .ReturnsAsync(authState);

        // Act
        await _themeService.InitializeAsync("dark");
        var effectiveTheme = await _themeService.GetEffectiveThemeAsync();
        var userPreference = await _themeService.GetUserPreferenceAsync();

        // Assert
        effectiveTheme.Should().Be("light");
        userPreference.Should().Be(ThemePreferenceConstants.Light);
    }

    [Fact]
    public async Task InitializeAsync_WithUserPreferringDark_UsesDark()
    {
        // Arrange
        var user = UserBuilder.Default()
            .WithId("user-123")
            .WithThemePreference(ThemePreferenceConstants.Dark)
            .Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-123")
        }, "test");
        var authenticatedUser = new ClaimsPrincipal(identity);
        var authState = new AuthenticationState(authenticatedUser);
        _authStateProviderMock.Setup(x => x.GetAuthenticationStateAsync())
            .ReturnsAsync(authState);

        // Act
        await _themeService.InitializeAsync("light");
        var effectiveTheme = await _themeService.GetEffectiveThemeAsync();
        var userPreference = await _themeService.GetUserPreferenceAsync();

        // Assert
        effectiveTheme.Should().Be("dark");
        userPreference.Should().Be(ThemePreferenceConstants.Dark);
    }

    [Fact]
    public async Task InitializeAsync_WithUserPreferringSystem_UsesSystemPreference()
    {
        // Arrange
        var user = UserBuilder.Default()
            .WithId("user-123")
            .WithThemePreference(null) // null means system
            .Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-123")
        }, "test");
        var authenticatedUser = new ClaimsPrincipal(identity);
        var authState = new AuthenticationState(authenticatedUser);
        _authStateProviderMock.Setup(x => x.GetAuthenticationStateAsync())
            .ReturnsAsync(authState);

        // Act
        await _themeService.InitializeAsync("dark");
        var effectiveTheme = await _themeService.GetEffectiveThemeAsync();
        var userPreference = await _themeService.GetUserPreferenceAsync();

        // Assert
        effectiveTheme.Should().Be("dark");
        userPreference.Should().Be(ThemePreferenceConstants.System);
    }

    [Fact]
    public async Task InitializeAsync_WithUserPreferringSystemButNoSystemPreference_UsesLight()
    {
        // Arrange
        var user = UserBuilder.Default()
            .WithId("user-123")
            .WithThemePreference(null) // null means system
            .Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-123")
        }, "test");
        var authenticatedUser = new ClaimsPrincipal(identity);
        var authState = new AuthenticationState(authenticatedUser);
        _authStateProviderMock.Setup(x => x.GetAuthenticationStateAsync())
            .ReturnsAsync(authState);

        // Act
        await _themeService.InitializeAsync(null);
        var effectiveTheme = await _themeService.GetEffectiveThemeAsync();

        // Assert
        effectiveTheme.Should().Be("light");
    }

    #endregion

    #region IsDarkMode Tests

    [Fact]
    public async Task IsDarkMode_WhenEffectiveThemeIsDark_ReturnsTrue()
    {
        // Arrange
        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        var authState = new AuthenticationState(anonymousUser);
        _authStateProviderMock.Setup(x => x.GetAuthenticationStateAsync())
            .ReturnsAsync(authState);

        // Act
        await _themeService.InitializeAsync("dark");

        // Assert
        _themeService.IsDarkMode.Should().BeTrue();
    }

    [Fact]
    public async Task IsDarkMode_WhenEffectiveThemeIsLight_ReturnsFalse()
    {
        // Arrange
        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        var authState = new AuthenticationState(anonymousUser);
        _authStateProviderMock.Setup(x => x.GetAuthenticationStateAsync())
            .ReturnsAsync(authState);

        // Act
        await _themeService.InitializeAsync("light");

        // Assert
        _themeService.IsDarkMode.Should().BeFalse();
    }

    #endregion

    #region SetUserPreferenceAsync Tests

    [Fact]
    public async Task SetUserPreferenceAsync_WithInvalidPreference_ReturnsFalse()
    {
        // Arrange
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-123")
        }, "test");
        var authenticatedUser = new ClaimsPrincipal(identity);
        var authState = new AuthenticationState(authenticatedUser);
        _authStateProviderMock.Setup(x => x.GetAuthenticationStateAsync())
            .ReturnsAsync(authState);

        // Act
        var result = await _themeService.SetUserPreferenceAsync("invalid");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SetUserPreferenceAsync_WithAnonymousUser_ReturnsFalse()
    {
        // Arrange
        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        var authState = new AuthenticationState(anonymousUser);
        _authStateProviderMock.Setup(x => x.GetAuthenticationStateAsync())
            .ReturnsAsync(authState);

        // Act
        var result = await _themeService.SetUserPreferenceAsync(ThemePreferenceConstants.Dark);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SetUserPreferenceAsync_WithNonExistentUser_ReturnsFalse()
    {
        // Arrange
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "non-existent-user")
        }, "test");
        var authenticatedUser = new ClaimsPrincipal(identity);
        var authState = new AuthenticationState(authenticatedUser);
        _authStateProviderMock.Setup(x => x.GetAuthenticationStateAsync())
            .ReturnsAsync(authState);

        // Act
        var result = await _themeService.SetUserPreferenceAsync(ThemePreferenceConstants.Dark);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SetUserPreferenceAsync_WithValidUserAndLightPreference_SavesAndReturnsTrue()
    {
        // Arrange
        var user = UserBuilder.Default()
            .WithId("user-123")
            .WithThemePreference(null)
            .Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-123")
        }, "test");
        var authenticatedUser = new ClaimsPrincipal(identity);
        var authState = new AuthenticationState(authenticatedUser);
        _authStateProviderMock.Setup(x => x.GetAuthenticationStateAsync())
            .ReturnsAsync(authState);

        // Act
        var result = await _themeService.SetUserPreferenceAsync(ThemePreferenceConstants.Light);

        // Assert
        result.Should().BeTrue();

        _dbContext.ChangeTracker.Clear(); // Clear cache to get fresh data from factory-created context
        var updatedUser = await _dbContext.Users.FindAsync("user-123");
        updatedUser.Should().NotBeNull();
        updatedUser!.ThemePreference.Should().Be(ThemePreferenceConstants.Light);
        updatedUser.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SetUserPreferenceAsync_WithValidUserAndDarkPreference_SavesAndReturnsTrue()
    {
        // Arrange
        var user = UserBuilder.Default()
            .WithId("user-123")
            .WithThemePreference(ThemePreferenceConstants.Light)
            .Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-123")
        }, "test");
        var authenticatedUser = new ClaimsPrincipal(identity);
        var authState = new AuthenticationState(authenticatedUser);
        _authStateProviderMock.Setup(x => x.GetAuthenticationStateAsync())
            .ReturnsAsync(authState);

        // Act
        var result = await _themeService.SetUserPreferenceAsync(ThemePreferenceConstants.Dark);

        // Assert
        result.Should().BeTrue();

        _dbContext.ChangeTracker.Clear();
        var updatedUser = await _dbContext.Users.FindAsync("user-123");
        updatedUser.Should().NotBeNull();
        updatedUser!.ThemePreference.Should().Be(ThemePreferenceConstants.Dark);
    }

    [Fact]
    public async Task SetUserPreferenceAsync_WithSystemPreference_SavesAsNull()
    {
        // Arrange
        var user = UserBuilder.Default()
            .WithId("user-123")
            .WithThemePreference(ThemePreferenceConstants.Dark)
            .Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-123")
        }, "test");
        var authenticatedUser = new ClaimsPrincipal(identity);
        var authState = new AuthenticationState(authenticatedUser);
        _authStateProviderMock.Setup(x => x.GetAuthenticationStateAsync())
            .ReturnsAsync(authState);

        // Act
        var result = await _themeService.SetUserPreferenceAsync(ThemePreferenceConstants.System);

        // Assert
        result.Should().BeTrue();

        _dbContext.ChangeTracker.Clear();
        var updatedUser = await _dbContext.Users.FindAsync("user-123");
        updatedUser.Should().NotBeNull();
        updatedUser!.ThemePreference.Should().BeNull(); // System preference stored as null
    }

    [Fact]
    public async Task SetUserPreferenceAsync_UpdatesEffectiveTheme()
    {
        // Arrange
        var user = UserBuilder.Default()
            .WithId("user-123")
            .WithThemePreference(ThemePreferenceConstants.Light)
            .Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-123")
        }, "test");
        var authenticatedUser = new ClaimsPrincipal(identity);
        var authState = new AuthenticationState(authenticatedUser);
        _authStateProviderMock.Setup(x => x.GetAuthenticationStateAsync())
            .ReturnsAsync(authState);

        await _themeService.InitializeAsync("light");

        // Act
        await _themeService.SetUserPreferenceAsync(ThemePreferenceConstants.Dark);
        var effectiveTheme = await _themeService.GetEffectiveThemeAsync();

        // Assert
        effectiveTheme.Should().Be("dark");
    }

    [Fact]
    public async Task SetUserPreferenceAsync_RaisesThemeChangedEvent()
    {
        // Arrange
        var user = UserBuilder.Default()
            .WithId("user-123")
            .WithThemePreference(ThemePreferenceConstants.Light)
            .Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-123")
        }, "test");
        var authenticatedUser = new ClaimsPrincipal(identity);
        var authState = new AuthenticationState(authenticatedUser);
        _authStateProviderMock.Setup(x => x.GetAuthenticationStateAsync())
            .ReturnsAsync(authState);

        var eventRaised = false;
        _themeService.ThemeChanged += (sender, args) => eventRaised = true;

        // Act
        await _themeService.SetUserPreferenceAsync(ThemePreferenceConstants.Dark);

        // Assert
        eventRaised.Should().BeTrue();
    }

    [Fact]
    public async Task SetUserPreferenceAsync_WithSystemPreferenceAndDarkSystem_UsesSystemDark()
    {
        // Arrange
        var user = UserBuilder.Default()
            .WithId("user-123")
            .WithThemePreference(ThemePreferenceConstants.Light)
            .Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-123")
        }, "test");
        var authenticatedUser = new ClaimsPrincipal(identity);
        var authState = new AuthenticationState(authenticatedUser);
        _authStateProviderMock.Setup(x => x.GetAuthenticationStateAsync())
            .ReturnsAsync(authState);

        await _themeService.InitializeAsync("dark");

        // Act
        await _themeService.SetUserPreferenceAsync(ThemePreferenceConstants.System);
        var effectiveTheme = await _themeService.GetEffectiveThemeAsync();

        // Assert
        effectiveTheme.Should().Be("dark"); // System is dark
    }

    #endregion

    #region GetEffectiveThemeAsync and GetUserPreferenceAsync Tests

    [Fact]
    public async Task GetEffectiveThemeAsync_ReturnsCurrentEffectiveTheme()
    {
        // Arrange
        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        var authState = new AuthenticationState(anonymousUser);
        _authStateProviderMock.Setup(x => x.GetAuthenticationStateAsync())
            .ReturnsAsync(authState);

        await _themeService.InitializeAsync("dark");

        // Act
        var effectiveTheme = await _themeService.GetEffectiveThemeAsync();

        // Assert
        effectiveTheme.Should().Be("dark");
    }

    [Fact]
    public async Task GetUserPreferenceAsync_ReturnsCurrentUserPreference()
    {
        // Arrange
        var user = UserBuilder.Default()
            .WithId("user-123")
            .WithThemePreference(ThemePreferenceConstants.Dark)
            .Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-123")
        }, "test");
        var authenticatedUser = new ClaimsPrincipal(identity);
        var authState = new AuthenticationState(authenticatedUser);
        _authStateProviderMock.Setup(x => x.GetAuthenticationStateAsync())
            .ReturnsAsync(authState);

        await _themeService.InitializeAsync("light");

        // Act
        var userPreference = await _themeService.GetUserPreferenceAsync();

        // Assert
        userPreference.Should().Be(ThemePreferenceConstants.Dark);
    }

    #endregion
}
