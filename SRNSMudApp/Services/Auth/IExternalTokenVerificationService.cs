namespace SRNSMudApp.Services.Auth;

public interface IExternalTokenVerificationService
{
    Task<(string? Email, string? ProviderKey)> VerifyTokenAsync(string provider, string token);
}
