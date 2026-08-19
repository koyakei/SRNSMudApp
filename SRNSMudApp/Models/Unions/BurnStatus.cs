namespace SRNSMudApp.Models.Unions;

public record Burned(DateTime BurnedAt);
public record NotBurned();

public union BurnStatus(Burned, NotBurned);
