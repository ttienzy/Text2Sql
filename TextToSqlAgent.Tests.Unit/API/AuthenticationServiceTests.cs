using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using TextToSqlAgent.API.Data;
using TextToSqlAgent.API.DTOs;
using TextToSqlAgent.API.Services;
using TextToSqlAgent.Infrastructure.Entities;
using Xunit;

namespace TextToSqlAgent.Tests.Unit.API;

/// <summary>
/// Unit tests for AuthenticationService
/// Validates Requirements 6.1, 6.2
/// </summary>
public class AuthenticationServiceTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly Mock<SignInManager<ApplicationUser>> _signInManagerMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<ILogger<AuthenticationService>> _loggerMock;
    private readonly AuthenticationService _authService;

    public AuthenticationServiceTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AppDbContext(options);

        // Setup mocks
        _userManagerMock = MockUserManager();
        _signInManagerMock = MockSignInManager();
        _configurationMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<AuthenticationService>>();

        // Setup configuration
        _configurationMock.Setup(c => c["Jwt:Key"]).Returns("SUPER_SECRET_KEY_FOR_DEV_ONLY_12345678");
        _configurationMock.Setup(c => c["Jwt:Issuer"]).Returns("TextToSqlAgentAPI");
        _configurationMock.Setup(c => c["Jwt:Audience"]).Returns("TextToSqlAgentClient");
        _configurationMock.Setup(c => c.GetValue("Jwt:AccessTokenExpiryMinutes", 180)).Returns(180);
        _configurationMock.Setup(c => c.GetValue("Jwt:RefreshTokenExpiryDays", 7)).Returns(7);

        _authService = new AuthenticationService(
            _userManagerMock.Object,
            _signInManagerMock.Object,
            _configurationMock.Object,
            _context,
            _loggerMock.Object);
    }

    [Fact]
    public async Task RegisterAsync_WithValidRequest_ShouldReturnSuccess()
    {
        // Arrange
        var request = new RegisterRequest
        {
            Email = "test@example.com",
            Password = "Password123!",
            FullName = "Test User"
        };

        _userManagerMock.Setup(um => um.FindByEmailAsync(request.Email))
            .ReturnsAsync((ApplicationUser?)null);

        _userManagerMock.Setup(um => um.CreateAsync(It.IsAny<ApplicationUser>(), request.Password))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _authService.RegisterAsync(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.NotEmpty(result.Data.AccessToken);
        Assert.NotEmpty(result.Data.RefreshToken);
        Assert.Equal(request.Email, result.Data.Email);
    }

    [Fact]
    public async Task RegisterAsync_WithExistingEmail_ShouldReturnFailure()
    {
        // Arrange
        var request = new RegisterRequest
        {
            Email = "existing@example.com",
            Password = "Password123!",
            FullName = "Test User"
        };

        var existingUser = new ApplicationUser { Email = request.Email };
        _userManagerMock.Setup(um => um.FindByEmailAsync(request.Email))
            .ReturnsAsync(existingUser);

        // Act
        var result = await _authService.RegisterAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("already exists", result.ErrorMessage);
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ShouldReturnSuccess()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "test@example.com",
            Password = "Password123!"
        };

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            Email = request.Email,
            UserName = request.Email,
            FullName = "Test User"
        };

        _userManagerMock.Setup(um => um.FindByEmailAsync(request.Email))
            .ReturnsAsync(user);

        _signInManagerMock.Setup(sm => sm.CheckPasswordSignInAsync(user, request.Password, false))
            .ReturnsAsync(SignInResult.Success);

        // Act
        var result = await _authService.LoginAsync(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.NotEmpty(result.Data.AccessToken);
        Assert.NotEmpty(result.Data.RefreshToken);
        Assert.Equal(request.Email, result.Data.Email);
    }

    [Fact]
    public async Task LoginAsync_WithInvalidCredentials_ShouldReturnFailure()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "test@example.com",
            Password = "WrongPassword"
        };

        var user = new ApplicationUser
        {
            Email = request.Email,
            UserName = request.Email
        };

        _userManagerMock.Setup(um => um.FindByEmailAsync(request.Email))
            .ReturnsAsync(user);

        _signInManagerMock.Setup(sm => sm.CheckPasswordSignInAsync(user, request.Password, false))
            .ReturnsAsync(SignInResult.Failed);

        // Act
        var result = await _authService.LoginAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Invalid email or password", result.ErrorMessage);
    }

    [Fact]
    public async Task RefreshTokenAsync_WithValidToken_ShouldReturnNewTokens()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            Email = "test@example.com",
            UserName = "test@example.com",
            FullName = "Test User"
        };

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid().ToString(),
            Token = "valid-refresh-token",
            UserId = user.Id,
            User = user,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            IsRevoked = false
        };

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync();

        // Act
        var result = await _authService.RefreshTokenAsync(refreshToken.Token);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.NotEmpty(result.Data.AccessToken);
        Assert.NotEmpty(result.Data.RefreshToken);
        Assert.NotEqual(refreshToken.Token, result.Data.RefreshToken); // Should be a new token
    }

    [Fact]
    public async Task RevokeTokenAsync_WithValidToken_ShouldReturnTrue()
    {
        // Arrange
        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid().ToString(),
            Token = "valid-refresh-token",
            UserId = Guid.NewGuid().ToString(),
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            IsRevoked = false
        };

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync();

        // Act
        var result = await _authService.RevokeTokenAsync(refreshToken.Token);

        // Assert
        Assert.True(result);

        // Verify token is revoked in database
        var revokedToken = await _context.RefreshTokens.FindAsync(refreshToken.Id);
        Assert.True(revokedToken?.IsRevoked);
        Assert.NotNull(revokedToken?.RevokedAt);
    }

    private static Mock<UserManager<ApplicationUser>> MockUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object, null, null, null, null, null, null, null, null);
    }

    private static Mock<SignInManager<ApplicationUser>> MockSignInManager()
    {
        var userManager = MockUserManager();
        var contextAccessor = new Mock<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        var claimsFactory = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();

        return new Mock<SignInManager<ApplicationUser>>(
            userManager.Object,
            contextAccessor.Object,
            claimsFactory.Object,
            null, null, null, null);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}