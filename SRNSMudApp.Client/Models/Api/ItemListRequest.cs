namespace SRNSMudApp.Client.Models.Api;

/// <summary>
/// タグフィルタ条件 DTO (TagId または TagName 指定)。
/// </summary>
public sealed record ItemListFilterDto(
    int? TagId = null,
    string? TagName = null,
    string? UserName = null
);

/// <summary>
/// ソート条件 DTO。
/// </summary>
public sealed record ItemListSortDto(int TagId, bool Ascending);

/// <summary>
/// ItemList クエリ API のリクエスト DTO。
/// </summary>
public sealed record ItemListQueryRequest(
    IReadOnlyList<ItemListFilterDto>? Filters = null,
    IReadOnlyList<ItemListSortDto>? Sorts = null,
    int Skip = 0,
    int Take = 50
);