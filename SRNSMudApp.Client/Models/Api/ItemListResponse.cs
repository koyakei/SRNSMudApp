namespace SRNSMudApp.Client.Models.Api;

/// <summary>
/// タグ情報 DTO。
/// </summary>
public sealed record TagDto(
    int Id,
    string Name,
    string? Content,
    string? OwnerUserName
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
    int? QuotedItemId = null
);

/// <summary>
/// ItemList クエリ API のレスポンス DTO。
/// </summary>
public sealed record ItemListQueryResponse(
    IReadOnlyList<ItemDto> Items,
    IReadOnlyList<TagDto> Tags,
    int TotalCount
);