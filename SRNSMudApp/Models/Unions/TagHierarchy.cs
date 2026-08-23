namespace SRNSMudApp.Models.Unions;

public record RootTag();
public record ChildTag(int ParentTagId);

public union TagHierarchy(RootTag, ChildTag);