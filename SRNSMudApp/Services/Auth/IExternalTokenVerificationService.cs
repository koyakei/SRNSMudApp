using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Services.Auth;

public interface IExternalTokenVerificationService
{
    Task<Result<ExternalTokenPayload>> VerifyTokenAsync(string provider, string token);
}