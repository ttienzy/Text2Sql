using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using TextToSqlAgent.API.Extensions;
using Xunit;

namespace TextToSqlAgent.Tests.Unit.API.Extensions;

public class HttpContextExtensionsTests
{
    [Fact]
    public void GetCurrentUserId_AuthenticatedUser_ReturnsUserId()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
        var user = new ClaimsPrincipal(identity);
        var context = new DefaultHttpContext { User = user };

        // Act
        var result = context.GetCurrentUserId();

        // Assert
        Assert.Equal(userId, result);
    }

    [Fact]
    public void GetCurrentUserId_UnauthenticatedUser_ReturnsNull()
    {
        // Arrange
        var context = new DefaultHttpContext();

        // Act
        var result = context.GetCurrentUserId();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetCurrentUserEmail_AuthenticatedUser_ReturnsEmail()
    {
        // Arrange
        var email = "test@example.com";
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.Email, email));
        var user = new ClaimsPrincipal(identity);
        var context = new DefaultHttpContext { User = user };

        // Act
        var result = context.GetCurrentUserEmail();

        // Assert
        Assert.Equal(email, result);
    }

    [Fact]
    public void IsUserAuthenticated_AuthenticatedUser_ReturnsTrue()
    {
        // Arrange
        var identity = new ClaimsIdentity("test");
        var user = new ClaimsPrincipal(identity);
        var context = new DefaultHttpContext { User = user };

        // Act
        var result = context.IsUserAuthenticated();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsUserAuthenticated_UnauthenticatedUser_ReturnsFalse()
    {
        // Arrange
        var context = new DefaultHttpContext();

        // Act
        var result = context.IsUserAuthenticated();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetRequiredUserId_AuthenticatedUser_ReturnsUserId()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
        var user = new ClaimsPrincipal(identity);
        var context = new DefaultHttpContext { User = user };

        // Act
        var result = context.GetRequiredUserId();

        // Assert
        Assert.Equal(userId, result);
    }

    [Fact]
    public void GetRequiredUserId_UnauthenticatedUser_ThrowsException()
    {
        // Arrange
        var context = new DefaultHttpContext();

        // Act & Assert
        Assert.Throws<UnauthorizedAccessException>(() => context.GetRequiredUserId());
    }

    [Fact]
    public void IsResourceOwner_SameUserId_ReturnsTrue()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
        var user = new ClaimsPrincipal(identity);
        var context = new DefaultHttpContext { User = user };

        // Act
        var result = context.IsResourceOwner(userId);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsResourceOwner_DifferentUserId_ReturnsFalse()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var resourceUserId = Guid.NewGuid().ToString();
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
        var user = new ClaimsPrincipal(identity);
        var context = new DefaultHttpContext { User = user };

        // Act
        var result = context.IsResourceOwner(resourceUserId);

        // Assert
        Assert.False(result);
    }
}