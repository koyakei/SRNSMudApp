using System.Diagnostics.CodeAnalysis;

namespace SRNSMudApp.Client.Models;

/// <summary>スタンドアロンアイテム。</summary>
public sealed record StandaloneItem;

/// <summary>アイテムへのリプライ。</summary>
public sealed record ReplyItem(int ParentItemId);

/// <summary>タグ付けリクエストへのリプライ。</summary>
public sealed record RequestReplyItem(int TaggingRequestEntityId);

/// <summary>タグ付けリクエストの本文アイテム。</summary>
public sealed record RequestBodyItem(int TaggingRequestEntityId);

/// <summary>引用アイテム。</summary>
public sealed record QuoteItem(int QuotedItemId);

/// <summary>タグコメントアイテム。</summary>
public sealed record TagCommentItem(int TargetItemId, int TagId);

/// <summary>アイテムの種類を表す union 型。</summary>
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public readonly union ItemKind(
    StandaloneItem,
    ReplyItem,
    RequestReplyItem,
    RequestBodyItem,
    QuoteItem,
    TagCommentItem);