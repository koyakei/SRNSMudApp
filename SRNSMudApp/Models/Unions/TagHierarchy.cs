using System.Diagnostics.CodeAnalysis;
namespace SRNSMudApp.Models.Unions;

public record RootTag();
public record ChildTag(int ParentTagId);

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public readonly union TagHierarchy(RootTag, ChildTag);