namespace SRNSMudApp.Components.Diagram;

using Microsoft.AspNetCore.Components;

using SRNSMudApp.Models;
using SRNSMudApp.Services;

/// <summary>
///     ダイアグラム上の ItemNode ウィジェットのコードビハインド。
///     ノードの展開・折りたたみ操作、詳細画面へのナビゲーション、リンクプレビューの解決を仲介する。
/// </summary>
public partial class ItemNodeWidget : ComponentBase
{
    /// <summary>
    ///     ダイアグラム上の Item ノード。
    /// </summary>
    [Parameter]
    public ItemNode Node { get; set; } = null!;

    /// <summary>
    ///     リンクプレビュー取得デリゲート（テスト用やカスタマイズ用に外部注入可能）。
    /// </summary>
    [Parameter]
    public Func<string, Task<LinkPreviewData?>>? LoadPreview { get; set; }

    [Inject]
    private NavigationManager NavigationManager { get; set; } = null!;

    [Inject]
    private ILinkPreviewService PreviewService { get; set; } = null!;

    private Task<LinkPreviewData?> GetPreviewAsync(string url) =>
        ItemNodeViewModel.ResolvePreviewAsync(url, Node.AllContextItems, PreviewService, LoadPreview);

    private void ToggleExpand()
    {
        Node.IsExpanded = !Node.IsExpanded;
        Node.Refresh();
    }

    private static void OnClick()
    {
        // 選択操作を妨げないため、シングルクリックではナビゲーションを行わない
    }

    private void NavigateToItemDetail()
    {
        NavigationManager.NavigateTo($"/ItemDetail/{Node.Item.Id}");
    }

    private void HandleHideNode()
    {
        Node.RequestHideNode?.Invoke(Node.Item);
    }
}