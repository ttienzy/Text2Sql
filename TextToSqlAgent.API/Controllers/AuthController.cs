using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using TextToSqlAgent.API.Data;
using TextToSqlAgent.API.DTOs;

namespace TextToSqlAgent.API.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<ApplicationUser> userManager, 
        IConfiguration configuration,
        ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("register")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest? request)
    {
        // ✅ FIX: Validate request is not null
        if (request == null)
        {
            return BadRequest(new { Message = "Request body is required" });
        }

        // ✅ FIX: Validate email
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new { Message = "Email is required" });
        }

        // ✅ FIX: Validate password
        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { Message = "Password is required" });
        }

        if (request.Password.Length < 6)
        {
            return BadRequest(new { Message = "Password must be at least 6 characters" });
        }

        try
        {
            _logger.LogInformation("Attempting to register user: {Email}", request.Email);
            
            var user = new ApplicationUser 
            { 
                UserName = request.Email, 
                Email = request.Email, 
                FullName = request.FullName 
            };
            
            var result = await _userManager.CreateAsync(user, request.Password);

            if (result.Succeeded)
            {
                _logger.LogInformation("User registered successfully: {Email}", request.Email);
                return Ok(new { Message = "User registered successfully" });
            }

            var errors = result.Errors.Select(e => e.Description).ToList();
            _logger.LogWarning("Registration failed for {Email}: {Errors}", request.Email, string.Join(", ", errors));
            return BadRequest(new { Errors = errors });
        }
        catch (Exception ex)
        {
            // ✅ FIX: Catch exception to prevent crash
            _logger.LogError(ex, "Registration failed with exception for {Email}", request.Email);
            return StatusCode(500, new { Message = $"Registration failed: {ex.Message}" });
        }
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Login([FromBody] LoginRequest? request)
    {
        // ✅ FIX: Validate request
        if (request == null)
        {
            return BadRequest(new { Message = "Request body is required" });
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new { Message = "Email is required" });
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { Message = "Password is required" });
        }

        try
        {
            _logger.LogInformation("Attempting login for: {Email}", request.Email);
            
            var user = await _userManager.FindByEmailAsync(request.Email);
            
            if (user == null)
            {
                _logger.LogWarning("Login failed - user not found: {Email}", request.Email);
                return Unauthorized(new { Message = "Invalid email or password" });
            }
            
            if (!await _userManager.CheckPasswordAsync(user, request.Password))
            {
                _logger.LogWarning("Login failed - invalid password for: {Email}", request.Email);
                return Unauthorized(new { Message = "Invalid email or password" });
            }

            var authClaims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.UserName!),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            };

            // ✅ FIX: Validate JWT key length
            var jwtKey = _configuration["Jwt:Key"] ?? "SUPER_SECRET_KEY_FOR_DEV_ONLY_12345678";
            if (jwtKey.Length < 32)
            {
                _logger.LogError("JWT key is too short");
                return StatusCode(500, new { Message = "JWT key is too short. Minimum 32 characters required." });
            }

            var authSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"] ?? "TextToSqlAgentAPI",
                audience: _configuration["Jwt:Audience"] ?? "TextToSqlAgentClient",
                expires: DateTime.Now.AddHours(3),
                claims: authClaims,
                signingCredentials: new SigningCredentials(authSigningKey, SecurityAlgorithms.HmacSha256)
            );

            _logger.LogInformation("Login successful for: {Email}", request.Email);
            
            return Ok(new AuthResponse
            {
                Token = new JwtSecurityTokenHandler().WriteToken(token),
                Expiration = token.ValidTo,
                Email = user.Email!
            });
        }
        catch (Exception ex)
        {
            // ✅ FIX: Catch exception to prevent crash
            _logger.LogError(ex, "Login failed with exception for {Email}", request.Email);
            return StatusCode(500, new { Message = $"Login failed: {ex.Message}" });
        }
    }
}
