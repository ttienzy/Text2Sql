using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;
using TextToSqlAgent.API.Authorization;
using Xunit;

namespace TextToSqlAgent.Tests.Unit.API.Authorization;

public class ValidUserAuthorizationHandlerTests
{
    private readonly Mock<ILogger<ValidUserAuthorizationHandler>> _mockLogger;
    private readonly ValidUserAuthorizationHandler _handler;

    public ValidUserAuthorizationHandlerTests()
    {
        _mockLogger = new Mock<ILogger<ValidUserAuthorizationHandler>>();
        _handler = new ValidUserAuthorizationHandler(_mockLogger.Object);
    }

    [Fact]
    public async Task HandleRequirementAsync_UnauthenticatedUser_Fails()
    {
        // Arrange
        var user = new ClaimsPrincipal();
        var context = new AuthorizationHandlerContext(new[] { new ValidUserRequirement() }, user, null);

        // Act
        await _handler.HandleRequirementAsync(context, new ValidUserRequirement());

        // Assert
        Assert.True(context.HasFailed);
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_MissingNameIdentifier_Fails()
    {
        // Arrange
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.Email, "test@example.com"));
        var user = new ClaimsPrincipal(identity);
        var context = new AuthorizationHandlerContext(new[] { new ValidUserRequirement() }, user, null);

        // Act
        await _handler.HandleRequirementAsync(context, new ValidUserRequirement());

        // Assert
        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task HandleRequirementAsync_MissingEmail_Fails()
    {
        // Arrange
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));
        var user = new ClaimsPrincipal(identity);
        var context = new AuthorizationHandlerContext(new[] { new ValidUserRequirement() }, user, null);

        // Act
        await _handler.HandleRequirementAsync(context, new ValidUserRequirement());

        // Assert
        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task HandleRequirementAsync_InvalidUserIdFormat_Fails()
    {
        // Arrange
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "invalid-guid"));
        identity.AddClaim(new Claim(ClaimTypes.Email, "test@example.com"));
        var user = new ClaimsPrincipal(identity);
        var context = new AuthorizationHandlerContext(new[] { new ValidUserRequirement() }, user, null);

        // Act
        await _handler.HandleRequirementAsync(context, new ValidUserRequirement());

        // Assert
        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task HandleRequirementAsync_ValidUser_Succeeds()
    {
        // Arrange
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Email, "test@example.com"));
        var user = new ClaimsPrincipal(identity);
        var context = new AuthorizationHandlerContext(new[] { new ValidUserRequirement() }, user, null);

        // Act
        await _handler.HandleRequirementAsync(context, new ValidUserRequirement());

        // Assert
        Assert.False(context.HasFailed);
        Assert.True(context.HasSucceeded);
    }
}

public class ResourceOwnerAuthorizationHandlerTests
{
    private readonly Mock<ILogger<ResourceOwnerAuthorizationHandler>> _mockLogger;
    private readonly ResourceOwnerAuthorizationHandler _handler;

    public ResourceOwnerAuthorizationHandlerTests()
    {
        _mockLogger = new Mock<ILogger<ResourceOwnerAuthorizationHandler>>();
        _handler = new ResourceOwnerAuthorizationHandler(_mockLogger.Object);
    }

    [Fact]
    public async Task HandleRequirementAsync_UnauthenticatedUser_Fails()
    {
        // Arrange
        var user = new ClaimsPrincipal();
        var httpContext = new DefaultHttpContext();
        var context = new AuthorizationHandlerContext(new[] { new ResourceOwnerRequirement() }, user, httpContext);

        // Act
        await _handler.HandleRequirementAsync(context, new ResourceOwnerRequirement());

        // Assert
        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task HandleRequirementAsync_ValidUserNoResourceId_Succeeds()
    {
        // Arrange
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));
        var user = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext();
        var context = new AuthorizationHandlerContext(new[] { new ResourceOwnerRequirement() }, user, httpContext);

        // Act
        await _handler.HandleRequirementAsync(context, new ResourceOwnerRequirement());

        // Assert
        Assert.False(context.HasFailed);
        Assert.True(context.HasSucceeded);
    }
}