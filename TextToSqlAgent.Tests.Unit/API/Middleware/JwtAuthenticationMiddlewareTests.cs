using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;
using TextToSqlAgent.API.Middleware;
using Xunit;

namespace TextToSqlAgent.Tests.Unit.API.Middleware;

public class JwtAuthenticationMiddlewareTests
{
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<ILogger<JwtAuthenticationMiddleware>> _mockLogger;
    private readonly Mock<RequestDelegate> _mockNext;

    public JwtAuthenticationMiddlewareTests()
    {
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<JwtAuthenticationMiddleware>>();
        _mockNext = new Mock<RequestDelegate>();

        // Setup JWT configuration
        _mockConfiguration.Setup(c => c["Jwt:Key"]).Returns("SUPER_SECRET_KEY_FOR_DEV_ONLY_12345678");
        _mockConfiguration.Setup(c => c["Jwt:Issuer"]).Returns("TextToSqlAgentAPI");
        _mockConfiguration.Setup(c => c["Jwt:Audience"]).Returns("TextToSqlAgentClient");
    }

    [Fact]
    public async Task InvokeAsync_NoAuthorizationHeader_CallsNext()
    {
        // Arrange
        var middleware = new JwtAuthenticationMiddleware(_mockNext.Object, _mockConfiguration.Object, _mockLogger.Object);
        var context = new DefaultHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        _mockNext.Verify(n => n(context), Times.Once);
        Assert.Null(context.User.Identity?.Name);
    }

    [Fact]
    public async Task InvokeAsync_InvalidToken_CallsNext()
    {
        // Arrange
        var middleware = new JwtAuthenticationMiddleware(_mockNext.Object, _mockConfiguration.Object, _mockLogger.Object);
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer invalid_token";

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        _mockNext.Verify(n => n(context), Times.Once);
        Assert.False(context.User.Identity?.IsAuthenticated);
    }
}
    [Fact]
    public async Task InvokeAsync_MalformedAuthorizationHeader_CallsNext()
    {
        // Arrange
        var middleware = new JwtAuthenticationMiddleware(_mockNext.Object, _mockConfiguration.Object, _mockLogger.Object);
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "InvalidFormat token";

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        _mockNext.Verify(n => n(context), Times.Once);
        Assert.False(context.User.Identity?.IsAuthenticated);
    }

    [Fact]
    public async Task InvokeAsync_EmptyBearerToken_CallsNext()
    {
        // Arrange
        var middleware = new JwtAuthenticationMiddleware(_mockNext.Object, _mockConfiguration.Object, _mockLogger.Object);
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer ";

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        _mockNext.Verify(n => n(context), Times.Once);
        Assert.False(context.User.Identity?.IsAuthenticated);
    }

    [Fact]
    public async Task InvokeAsync_ExceptionDuringValidation_CallsNext()
    {
        // Arrange
        var mockConfig = new Mock<IConfiguration>();
        mockConfig.Setup(c => c["Jwt:Key"]).Returns((string?)null); // This will cause an exception

        var middleware = new JwtAuthenticationMiddleware(_mockNext.Object, mockConfig.Object, _mockLogger.Object);
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer some_token";

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        _mockNext.Verify(n => n(context), Times.Once);
        Assert.False(context.User.Identity?.IsAuthenticated);
    }
}