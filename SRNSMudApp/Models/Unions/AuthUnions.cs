namespace SRNSMudApp.Models.Unions;

public record ExternalTokenPayload(string? Email, string? ProviderKey);

public union ExternalTokenVerificationResult(Success<ExternalTokenPayload>, Failure);
