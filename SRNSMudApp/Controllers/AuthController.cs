#pragma warning disable CA1848

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
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
        return await ((string.IsNullOrWhiteSpace(request.Provider) || string.IsNullOrWhiteSpace(request.Token)) switch
        {
            true => Task.FromResult<IActionResult>(BadRequest("Provider and Token are required.")),
            false => ProcessTokenVerificationAsync(request)
        });
    }

    private async Task<IActionResult> ProcessTokenVerificationAsync(ExternalLoginRequest request)
    {
        Result<ExternalTokenPayload> result = await _tokenService.VerifyTokenAsync(request.Provider, request.Token);
        return await (result switch
        {
            Failure => Task.FromResult<IActionResult>(Unauthorized("Invalid token.")),
            Success<ExternalTokenPayload> { Value.ProviderKey: null } => Task.FromResult<IActionResult>(Unauthorized("Invalid token.")),
            Success<ExternalTokenPayload> success => ProcessRiskAssessmentAsync(request, success.Value),
            _ => Task.FromResult<IActionResult>(Unauthorized("Invalid token."))
        });
    }

    private async Task<IActionResult> ProcessRiskAssessmentAsync(ExternalLoginRequest request, ExternalTokenPayload payload)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var isRisky = await _riskService.IsRequestRiskyAsync(ip, request.DeviceId, payload.Email);

        return await (isRisky switch
        {
            true => Task.FromResult<IActionResult>(Forbid("Risk assessment failed.")),
            false => ProcessUserLoginAsync(request, payload)
        });
    }

    private async Task<IActionResult> ProcessUserLoginAsync(ExternalLoginRequest request, ExternalTokenPayload payload)
    {
        var userLoginInfo = new UserLoginInfo(request.Provider, payload.ProviderKey, request.Provider);
        ApplicationUser? existingUser = await _userManager.FindByLoginAsync(request.Provider, payload.ProviderKey);

        existingUser = existingUser switch
        {
            null when !string.IsNullOrEmpty(payload.Email) => await _userManager.FindByEmailAsync(payload.Email),
            _ => existingUser
        };

        return await (existingUser switch
        {
            null => CreateNewUserAsync(payload, userLoginInfo),
            _ => LinkAndSignInAsync(existingUser, userLoginInfo, request.Provider)
        });
    }

    private async Task<IActionResult> CreateNewUserAsync(ExternalTokenPayload payload, UserLoginInfo userLoginInfo)
    {
        var newUser = new ApplicationUser
        {
            UserName = payload.Email ?? $"user_{Guid.NewGuid():N}",
            Email = payload.Email,
            EmailConfirmed = true
        };

        IdentityResult createResult = await _userManager.CreateAsync(newUser);

        return await (createResult.Succeeded switch
        {
            false => HandleCreateUserError(createResult),
            true => AddLoginAndSignInAsync(newUser, userLoginInfo)
        });
    }

    private Task<IActionResult> HandleCreateUserError(IdentityResult createResult)
    {
        _logger.LogError("Failed to create user: {Errors}",
            string.Join(", ", createResult.Errors.Select(e => e.Description)));
        return Task.FromResult<IActionResult>(StatusCode(500, "Error creating user account."));
    }

    private async Task<IActionResult> AddLoginAndSignInAsync(ApplicationUser newUser, UserLoginInfo userLoginInfo)
    {
        IdentityResult addLoginResult = await _userManager.AddLoginAsync(newUser, userLoginInfo);

        _ = addLoginResult.Succeeded switch
        {
            false => (object)Task.Run(() => _logger.LogError("Failed to add login to user: {Errors}",
                string.Join(", ", addLoginResult.Errors.Select(e => e.Description)))),
            true => null
        };

        await _signInManager.SignInAsync(newUser, true);
        return Ok(new { success = true });
    }

    private async Task<IActionResult> LinkAndSignInAsync(ApplicationUser existingUser, UserLoginInfo userLoginInfo, string provider)
    {
        IList<UserLoginInfo> logins = await _userManager.GetLoginsAsync(existingUser);

        _ = logins.Any(l => l.LoginProvider == provider && l.ProviderKey == userLoginInfo.ProviderKey) switch
        {
            false => await _userManager.AddLoginAsync(existingUser, userLoginInfo),
            true => null
        };

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