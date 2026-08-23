namespace SRNSMudApp.Models.Unions;

public record RejectionReason(string Reason);
public record NoRejection();

public union RejectionInfo(RejectionReason, NoRejection);