using System.Diagnostics.CodeAnalysis;
namespace SRNSMudApp.Models.Unions;

public record PendingInput(string CredentialJson);
public record InputError(string Error);

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public union PasskeyState(PendingInput, InputError);