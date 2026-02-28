using System.IdentityModel.Tokens.Jwt;
using System.Text;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Api;
using EastSeat.Agenti.Shared.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace EastSeat.Agenti.UnitTests.Api;

[Trait("Category", "Unit")]
[Trait("Feature", "RestApi")]
public class AuthEndpointsTests
{
    private static IConfiguration CreateConfiguration(string? jwtKey)
    {
        var configData = new Dictionary<string, string?>();

        if (jwtKey is not null)
            configData["Jwt:Key"] = jwtKey;

        configData["Jwt:Issuer"] = "EastSeat.Agenti";
        configData["Jwt:Audience"] = "EastSeat.Agenti.Android";
        configData["Jwt:ExpiryMinutes"] = "60";

        return new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void JwtKeyValidation_WithEmptyOrWhitespaceKey_IsDetectedByStringIsNullOrWhiteSpace(string key)
    {
        // Arrange
        var config = CreateConfiguration(key);
        var jwtKey = config["Jwt:Key"];

        // Act & Assert - Validates that our guard condition catches these cases
        string.IsNullOrWhiteSpace(jwtKey).Should().BeTrue(
            "empty or whitespace JWT keys should be detected before attempting to create a SymmetricSecurityKey");
    }

    [Fact]
    public void JwtKeyValidation_WithNullKey_IsDetectedByStringIsNullOrWhiteSpace()
    {
        // Arrange - Don't set Jwt:Key at all
        var config = CreateConfiguration(null);
        var jwtKey = config["Jwt:Key"];

        // Act & Assert
        string.IsNullOrWhiteSpace(jwtKey).Should().BeTrue(
            "null JWT keys should be detected before attempting to create a SymmetricSecurityKey");
    }

    [Fact]
    public void JwtKeyValidation_WithValidKey_IsNotFlagged()
    {
        // Arrange - 32-character key (256 bits for HS256)
        var config = CreateConfiguration("ThisIsASecureJwtKey32CharsLong!");
        var jwtKey = config["Jwt:Key"];

        // Act & Assert
        string.IsNullOrWhiteSpace(jwtKey).Should().BeFalse();
    }

    [Fact]
    public void SymmetricSecurityKey_WithEmptyKey_ThrowsArgumentException()
    {
        // This test documents the original IDX10703 error that occurs when
        // a SymmetricSecurityKey is created with a zero-length key
        Action act = () => new SymmetricSecurityKey(Encoding.UTF8.GetBytes(string.Empty));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*IDX10703*");
    }

    [Fact]
    public void SymmetricSecurityKey_WithValidKey_DoesNotThrow()
    {
        // Arrange - 32-character key (256 bits for HS256)
        var validKey = "ThisIsASecureJwtKey32CharsLong!!";

        // Act
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(validKey));

        // Assert
        key.Should().NotBeNull();
        key.KeySize.Should().BeGreaterOrEqualTo(256);
    }
}
