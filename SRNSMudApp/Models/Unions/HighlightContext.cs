namespace SRNSMudApp.Models.Unions;

public record NoHighlight();
public record TagHighlight(int TagId);

public union HighlightContext(NoHighlight, TagHighlight);