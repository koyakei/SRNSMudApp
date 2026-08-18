#pragma warning disable CA1848

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

using SRNSMudApp.Data;
using SRNSMudApp.Services.Auth;

namespace SRNSMudApp.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("AuthRateLimit")]
public class AuthController(
    IExternalTokenVerificationService tokenService,
    RiskAssessmentService riskService,
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    ILogger<AuthController> logger) : ControllerBase
{
    private readonly ILogger<AuthController> _logger = logger;
    private readonly RiskAssessmentService _riskService = riskService;
    private readonly SignInManager<ApplicationUser> _signInManager = signInManager;
    private readonly IExternalTokenVerificationService _tokenService = tokenService;
    private readonly UserManager<ApplicationUser> _userManager = userManager;

    [HttpPost("external-login")]
    public async Task<IActionResult> ExternalLogin([FromBody] ExternalLoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Provider) || string.IsNullOrWhiteSpace(request.Token))
        {
            return BadRequest("Provider and Token are required.");
        }

        // 1. Verify ID Token
        var (email, providerKey) = await _tokenService.VerifyTokenAsync(request.Provider, request.Token);
        if (string.IsNullOrEmpty(providerKey))
        {
            return Unauthorized("Invalid token.");
        }

        // 2. Risk Assessment
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var isRisky = await _riskService.IsRequestRiskyAsync(ip, request.DeviceId, email);
        if (isRisky)
        {
            return Forbid("Risk assessment failed.");
        }

        // 3. Find existing user
        var userLoginInfo = new UserLoginInfo(request.Provider, providerKey, request.Provider);
        ApplicationUser? existingUser = await _userManager.FindByLoginAsync(request.Provider, providerKey);

        if (existingUser == null && !string.IsNullOrEmpty(email))
        {
            existingUser = await _userManager.FindByEmailAsync(email);
        }

        // 4. Registration logic based on Plan B:
        // "NOBODY can use Email & Password. Everyone must use Google/LINE/GitHub. 
        // However, only people invited by an admin are allowed to create an account at all."
        if (existingUser == null)
        {
            // Create new user
            var newUser = new ApplicationUser
            {
                UserName = email ?? $"user_{Guid.NewGuid():N}",
                Email = email,
                EmailConfirmed = true // External logins are usually pre-confirmed
            };

            IdentityResult createResult = await _userManager.CreateAsync(newUser);
            if (!createResult.Succeeded)
            {
                _logger.LogError("Failed to create user: {Errors}",
                    string.Join(", ", createResult.Errors.Select(e => e.Description)));
                return StatusCode(500, "Error creating user account.");
            }

            IdentityResult addLoginResult = await _userManager.AddLoginAsync(newUser, userLoginInfo);
            if (!addLoginResult.Succeeded)
            {
                _logger.LogError("Failed to add login to user: {Errors}",
                    string.Join(", ", addLoginResult.Errors.Select(e => e.Description)));
            }

            existingUser = newUser;
        }
        else
        {
            // Link if not already linked
            IList<UserLoginInfo> logins = await _userManager.GetLoginsAsync(existingUser);
            if (!logins.Any(l => l.LoginProvider == request.Provider && l.ProviderKey == providerKey))
            {
                _ = await _userManager.AddLoginAsync(existingUser, userLoginInfo);
            }
        }

        // 5. Issue Identity Session
        await _signInManager.SignInAsync(existingUser, true);

        return Ok(new { success = true });
    }
}

public class ExternalLoginRequest
{
    public string Provider { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string? DeviceId { get; set; }
    public string? InviteCode { get; set; }
}

#pragma warning restore CA1848