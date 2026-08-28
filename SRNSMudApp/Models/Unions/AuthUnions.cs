using System.Diagnostics.CodeAnalysis;
namespace SRNSMudApp.Models.Unions;

public record ExternalTokenPayload(string? Email, string? ProviderKey);

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public readonly union ExternalTokenVerificationResult(Success<ExternalTokenPayload>, Failure);