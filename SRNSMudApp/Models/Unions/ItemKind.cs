namespace SRNSMudApp.Models.Unions;

public record StandaloneItem();
public record ReplyItem(int ParentItemId);
public record RequestReplyItem(int TaggingRequestEntityId);
public record RequestBodyItem(int TaggingRequestEntityId);

public union ItemKind(StandaloneItem, ReplyItem, RequestReplyItem, RequestBodyItem);