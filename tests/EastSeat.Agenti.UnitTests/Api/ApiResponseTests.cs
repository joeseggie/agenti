using EastSeat.Agenti.Web.Features.Api;
using FluentAssertions;

namespace EastSeat.Agenti.UnitTests.Api;

[Trait("Category", "Unit")]
[Trait("Feature", "RestApi")]
public class ApiResponseTests
{
    [Fact]
    public void ApiResponse_Ok_SetsSuccessTrueAndData()
    {
        // Arrange
        var data = new LoginResponse
        {
            AccessToken = "test-token",
            UserId = "user-1",
            Email = "agent@test.com",
            FullName = "Test Agent",
            Role = "Agent"
        };

        // Act
        var response = ApiResponse<LoginResponse>.Ok(data);

        // Assert
        response.Success.Should().BeTrue();
        response.Data.Should().Be(data);
        response.Error.Should().BeNull();
    }

    [Fact]
    public void ApiResponse_Fail_SetsSuccessFalseAndError()
    {
        // Arrange
        const string errorMessage = "Invalid credentials.";

        // Act
        var response = ApiResponse<LoginResponse>.Fail(errorMessage);

        // Assert
        response.Success.Should().BeFalse();
        response.Data.Should().BeNull();
        response.Error.Should().Be(errorMessage);
    }

    [Fact]
    public void LoginResponse_DefaultValues_AreCorrect()
    {
        // Act
        var response = new LoginResponse();

        // Assert
        response.TokenType.Should().Be("Bearer");
        response.AccessToken.Should().BeEmpty();
        response.UserId.Should().BeEmpty();
    }

    [Fact]
    public void LoginRequest_RequiredFields_CanBeSet()
    {
        // Arrange & Act
        var request = new LoginRequest
        {
            Email = "agent@bank.com",
            Password = "SecurePassword123!"
        };

        // Assert
        request.Email.Should().Be("agent@bank.com");
        request.Password.Should().Be("SecurePassword123!");
    }
}
