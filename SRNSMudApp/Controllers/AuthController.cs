using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services.Auth;

namespace SRNSMudApp.Controllers;

/// <summary>
/// Handles external authentication requests from social identity providers.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("AuthRateLimit")]
public partial class AuthController(
    IExternalTokenVerificationService tokenService,
    RiskAssessmentService riskService,
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    ILogger<AuthController> logger) : ControllerBase
{
    /// <summary>
    /// Verifies an external identity provider token and signs the user in.
    /// </summary>
    /// <param name="request">The external login request payload.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the request to complete.</param>
    /// <returns>An <see cref="IActionResult"/> indicating the login outcome.</returns>
    [HttpPost("external-login")]
    public async Task<IActionResult> ExternalLogin([FromBody] ExternalLoginRequest request, CancellationToken cancellationToken)
    {
        return await ProcessTokenVerificationAsync(request, cancellationToken);
    }

    private async Task<IActionResult> ProcessTokenVerificationAsync(ExternalLoginRequest request, CancellationToken cancellationToken)
    {
        Result<ExternalTokenPayload> result = await tokenService.VerifyTokenAsync(request.Provider, request.Token, cancellationToken);

        if (result is not Success<ExternalTokenPayload> success || success.Value.ProviderKey is null)
        {
            return Unauthorized("Invalid token.");
        }

        return await ProcessRiskAssessmentAsync(request, success.Value, cancellationToken);
    }

    private async Task<IActionResult> ProcessRiskAssessmentAsync(ExternalLoginRequest request, ExternalTokenPayload payload, CancellationToken cancellationToken)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var isRisky = await riskService.IsRequestRiskyAsync(ip, request.DeviceId, payload.Email, cancellationToken);

        if (isRisky)
        {
            return Forbid("Risk assessment failed.");
        }

        return await ProcessUserLoginAsync(request, payload, cancellationToken);
    }

    private async Task<IActionResult> ProcessUserLoginAsync(ExternalLoginRequest request, ExternalTokenPayload payload, CancellationToken cancellationToken)
    {
        var userLoginInfo = new UserLoginInfo(request.Provider, payload.ProviderKey, request.Provider);
        ApplicationUser? existingUser = await userManager.FindByLoginAsync(request.Provider, payload.ProviderKey);

        if (existingUser is null && !string.IsNullOrEmpty(payload.Email))
        {
            existingUser = await userManager.FindByEmailAsync(payload.Email);
        }

        if (existingUser is null)
        {
            return await CreateNewUserAsync(payload, userLoginInfo, cancellationToken);
        }

        return await LinkAndSignInAsync(existingUser, userLoginInfo, request.Provider, cancellationToken);
    }

    private async Task<IActionResult> CreateNewUserAsync(ExternalTokenPayload payload, UserLoginInfo userLoginInfo, CancellationToken cancellationToken)
    {
        var newUser = new ApplicationUser
        {
            UserName = payload.Email ?? $"user_{Guid.NewGuid():N}",
            Email = payload.Email,
            EmailConfirmed = true
        };

        IdentityResult createResult = await userManager.CreateAsync(newUser);

        if (!createResult.Succeeded)
        {
            return HandleCreateUserError(createResult);
        }

        return await AddLoginAndSignInAsync(newUser, userLoginInfo, cancellationToken);
    }

    private ObjectResult HandleCreateUserError(IdentityResult createResult)
    {
        var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
        LogFailedToCreateUser(errors);
        return StatusCode(500, "Error creating user account.");
    }

    private async Task<IActionResult> AddLoginAndSignInAsync(ApplicationUser newUser, UserLoginInfo userLoginInfo, CancellationToken cancellationToken)
    {
        IdentityResult addLoginResult = await userManager.AddLoginAsync(newUser, userLoginInfo);

        if (!addLoginResult.Succeeded)
        {
            LogFailedToAddLogin(string.Join(", ", addLoginResult.Errors.Select(e => e.Description)));
        }

        await signInManager.SignInAsync(newUser, true);
        return Ok(new { success = true });
    }

    private async Task<IActionResult> LinkAndSignInAsync(ApplicationUser existingUser, UserLoginInfo userLoginInfo, string provider, CancellationToken cancellationToken)
    {
        IList<UserLoginInfo> logins = await userManager.GetLoginsAsync(existingUser);

        if (!logins.Any(l => l.LoginProvider == provider && l.ProviderKey == userLoginInfo.ProviderKey))
        {
            _ = await userManager.AddLoginAsync(existingUser, userLoginInfo);
        }

        await signInManager.SignInAsync(existingUser, true);
        return Ok(new { success = true });
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Error, Message = "Failed to create user: {Errors}")]
    private partial void LogFailedToCreateUser(string errors);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error, Message = "Failed to add login to user: {Errors}")]
    private partial void LogFailedToAddLogin(string errors);
}
