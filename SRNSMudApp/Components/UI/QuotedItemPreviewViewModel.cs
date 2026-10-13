namespace SRNSMudApp.Components.UI;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

/// <summary>
///     QuotedItemPreview コンポーネントの状態管理および引用元アイテム取得ロジックを担う ViewModel。
///     子コンポーネントごとに独立した状態を保持するため Transient で管理する。
/// </summary>
public sealed class QuotedItemPreviewViewModel
{
    private readonly IItemQuoteService _itemQuoteService;
    private int _loadedQuotedItemId;
    private Item? _loadedQuotedItem;

    public QuotedItemPreviewViewModel(IItemQuoteService itemQuoteService)
    {
        _itemQuoteService = itemQuoteService;
    }

    public Item? TargetItem { get; private set; }
    public bool IsLoading { get; private set; }
    public int QuotedItemId { get; private set; }

    /// <summary>
    ///     本文をテキストおよび URL セグメントに分割したコレクション。
    /// </summary>
    public IReadOnlyList<ContentSegment> Segments { get; private set; } = [];

    /// <summary>
    ///     詳細画面への遷移先相対URL。
    /// </summary>
    [SuppressMessage("Design", "CA1056:UriPropertiesShouldNotBeStrings", Justification = "NavigationManager relative URI path string")]
    public string? NavigationUrl => QuotedItemId > 0 ? $"/ItemDetail/{QuotedItemId}" : null;

    /// <summary>
    ///     引用元アイテムの読み込みまたはパラメータ更新を行う。
    /// </summary>
    public async Task UpdateParametersAsync(int quotedItemId, Item? quotedItem)
    {
        if (ReferenceEquals(_loadedQuotedItem, quotedItem) && _loadedQuotedItemId == quotedItemId)
        {
            return;
        }

        _loadedQuotedItem = quotedItem;
        _loadedQuotedItemId = quotedItemId;
        QuotedItemId = quotedItemId;
        TargetItem = quotedItem;
        UpdateSegments();

        if (TargetItem is null && quotedItemId > 0)
        {
            IsLoading = true;
            try
            {
                TargetItem = await _itemQuoteService.GetQuotedItemAsync(quotedItemId);
                UpdateSegments();
            }
            finally
            {
                IsLoading = false;
            }
        }
    }

    private void UpdateSegments()
    {
        var content = TargetItem?.Content;
        Segments = string.IsNullOrWhiteSpace(content)
            ? []
            : ItemCardViewModel.GetContentSegments(content);
    }
}