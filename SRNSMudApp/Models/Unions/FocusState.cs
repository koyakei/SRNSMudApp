namespace SRNSMudApp.Models.Unions;

public record NoFocus();
public record FocusedItem(int ItemId);

public union FocusState(NoFocus, FocusedItem);