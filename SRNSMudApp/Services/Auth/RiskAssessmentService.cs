#pragma warning disable CA1848, CA1873

namespace SRNSMudApp.Services.Auth;

public class RiskAssessmentService(ILogger<RiskAssessmentService> logger)
{
    private readonly ILogger<RiskAssessmentService> _logger = logger;

    /// <summary>
    ///     Evaluates the risk of the current authentication request.
    ///     Currently a placeholder for future risk assessment rules (IP, Device, User behavior).
    /// </summary>
    public Task<bool> IsRequestRiskyAsync(string? ipAddress, string? deviceId, string? userEmail)
    {
        // Add specific rules here in the future
        _logger.LogInformation("Risk assessment passed for IP: {IP}, Device: {Device}, User: {User}", ipAddress,
            deviceId, userEmail);

        // Return false meaning "Not Risky"
        return Task.FromResult(false);
    }
}

#pragma warning restore CA1848, CA1873