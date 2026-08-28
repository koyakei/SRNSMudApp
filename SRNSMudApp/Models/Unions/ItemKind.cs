using System.Diagnostics.CodeAnalysis;
namespace SRNSMudApp.Models.Unions;

public record StandaloneItem();
public record ReplyItem(int ParentItemId);
public record RequestReplyItem(int TaggingRequestEntityId);
public record RequestBodyItem(int TaggingRequestEntityId);

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public readonly union ItemKind(StandaloneItem, ReplyItem, RequestReplyItem, RequestBodyItem);