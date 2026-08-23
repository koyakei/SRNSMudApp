namespace SRNSMudApp.Models.Unions;

public record PendingInput(string CredentialJson);
public record InputError(string Error);

public union PasskeyState(PendingInput, InputError);