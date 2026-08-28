using System.Diagnostics.CodeAnalysis;

using SRNSMudApp.Data;

namespace SRNSMudApp.Models.Unions;

public record NoTagSelected();
public record TagSelected(Tag SelectedTag);

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public readonly union TagSelection(NoTagSelected, TagSelected);