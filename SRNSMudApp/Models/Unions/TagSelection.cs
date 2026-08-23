using SRNSMudApp.Data;

namespace SRNSMudApp.Models.Unions;

public record NoTagSelected();
public record TagSelected(Tag SelectedTag);

public union TagSelection(NoTagSelected, TagSelected);