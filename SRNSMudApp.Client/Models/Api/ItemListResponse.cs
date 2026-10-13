namespace SRNSMudApp.Client.Models.Api;

/// <summary>
/// タグ情報 DTO。
/// </summary>
public sealed record TagDto(
    int Id,
    string Name,
    string? Content,
    string? OwnerUserName,
    string? OwnerUserId = null,
    int Score = 0,
    bool IsPrivate = false,
    string? TargetUserGroupName = null,
    IReadOnlyList<TagRelationDto>? TargetTagRelations = null
);

/// <summary>
/// アイテムに紐づくタグ関連付け情報 DTO。
/// </summary>
public sealed record TagRelationDto(
    int TagId,
    string TagName,
    int? Weight,
    string? OwnerUserName
);

/// <summary>
/// アイテム情報 DTO。
/// </summary>
public sealed record ItemDto(
    int Id,
    string Content,
    string? OwnerUserName,
    DateTimeOffset UpdatedDate,
    bool IsPrivate,
    IReadOnlyList<TagRelationDto> TagRelations,
    string ItemKindJson,
    int? ParentItemId = null,
    int? RootItemId = null,
    int? QuotedItemId = null,
    string? OwnerUserId = null,
    int Score = 0,
    int ShinjiScore = 0,
    int ZenScore = 0,
    int BiScore = 0,
    int ReplyCount = 0,
    int QuoteCount = 0,
    string? QuotedItemContent = null,
    string? QuotedItemOwnerUserName = null,
    string? TargetUserGroupName = null
);

/// <summary>
/// ItemList クエリ API のレスポンス DTO。
/// </summary>
public sealed record ItemListQueryResponse(
    IReadOnlyList<ItemDto> Items,
    IReadOnlyList<TagDto> Tags,
    int TotalCount
);