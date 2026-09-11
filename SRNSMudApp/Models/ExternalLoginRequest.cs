using System.ComponentModel.DataAnnotations;

namespace SRNSMudApp.Models;

/// <summary>
/// Represents a request to authenticate using an external identity provider.
/// </summary>
public class ExternalLoginRequest
{
    /// <summary>
    /// Gets or sets the external login provider name.
    /// </summary>
    [Required]
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the provider-issued authentication token.
    /// </summary>
    [Required]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the client device identifier, if available.
    /// </summary>
    public string? DeviceId { get; set; }

    /// <summary>
    /// Gets or sets the invite code associated with the login request, if any.
    /// </summary>
    public string? InviteCode { get; set; }
}
