namespace SRNSMudApp.Models.Unions;

public record ItemTarget(int TargetItemId);
public record TagTarget(int TargetTagId);

public union TimelineTarget(ItemTarget, TagTarget);