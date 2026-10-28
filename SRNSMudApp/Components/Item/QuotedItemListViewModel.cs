using System.Diagnostics.CodeAnalysis;

using SRNSMudApp.Services;

namespace SRNSMudApp.Components.Item;

using DataItem = SRNSMudApp.Data.Item;

/// <summary>
///     QuotedItemListDialog の状態管理、引用元アイテムおよび引用一覧のデータ取得を担当する ViewModel。
///     UI（Blazor / bUnit）から切り離し、純粋な xUnit + Moq 単体テストを可能にする。
///     プライマリコンストラクタにより依存サービスを簡潔に注入する。
/// </summary>
public class QuotedItemListViewModel(IItemQuoteService itemQuoteService)
{
    public DataItem? TargetItem { get; set; }
    public DataItem? SourceItem { get; private set; }
    public IReadOnlyList<DataItem> QuotedItems { get; private set; } = [];
    public bool IsLoading { get; private set; } = true;

    public bool HasSourceItem => SourceItem != null;
    public bool HasQuotedItems => QuotedItems.Count > 0;
    public bool ShowNoQuotesMessage => !IsLoading && !HasSourceItem && !HasQuotedItems;

    /// <summary>
    ///     引用元アイテムおよび引用されたアイテム一覧を非同期取得する。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "例外発生時にも画面クラッシュを防ぎ、ローディング状態を解除するため捕捉する")]
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (TargetItem == null)
        {
            SourceItem = null;
            QuotedItems = [];
            IsLoading = false;
            return;
        }

        IsLoading = true;
        try
        {
            SourceItem = await itemQuoteService.GetSourceItemAsync(TargetItem.Id, cancellationToken);
            QuotedItems = await itemQuoteService.GetQuotedByItemsAsync(TargetItem.Id, cancellationToken);
        }
        catch
        {
            SourceItem = null;
            QuotedItems = [];
        }
        finally
        {
            IsLoading = false;
        }
    }
}