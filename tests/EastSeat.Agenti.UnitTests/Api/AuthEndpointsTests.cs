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

    [Fact]
    public void CreateConfiguration_WithNullKey_DoesNotSetJwtKey()
    {
        // Arrange
        var config = CreateConfiguration(null);

        // Act
        var jwtKey = config["Jwt:Key"];

        // Assert
        jwtKey.Should().BeNull("Jwt:Key should not be present in configuration when no key is provided");
    }

    [Fact]
    public void CreateConfiguration_WithNonNullKey_SetsJwtKeyCorrectly()
    {
        // Arrange
        const string expectedKey = "ThisIsASecureJwtKey32CharsLong!";

        // Act
        var config = CreateConfiguration(expectedKey);
        var jwtKey = config["Jwt:Key"];

        // Assert
        jwtKey.Should().Be(expectedKey);
    }

    [Fact]
    public void CreateConfiguration_AlwaysSetsIssuerAudienceAndExpiry()
    {
        // Arrange
        var config = CreateConfiguration(null);

        // Act & Assert
        config["Jwt:Issuer"].Should().Be("EastSeat.Agenti");
        config["Jwt:Audience"].Should().Be("EastSeat.Agenti.Android");
        config["Jwt:ExpiryMinutes"].Should().Be("60");
    }
}
