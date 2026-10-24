namespace SRNSMudApp.Components.UI;

using System;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

/// <summary>
///     引用されたアイテムの要約プレビューを表示するコンポーネント。
///     データ取得およびナビゲーションURL計算は <see cref="QuotedItemPreviewViewModel"/> に委譲する。
/// </summary>
public partial class QuotedItemPreview : ComponentBase
{
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;
    [Inject] private QuotedItemPreviewViewModel ViewModel { get; set; } = null!;
    [Inject] private ILinkPreviewService LinkPreviewService { get; set; } = null!;

    [Parameter] public int QuotedItemId { get; set; }
    [Parameter] public Item? QuotedItem { get; set; }

    /// <summary>
    ///     リンクプレビュー取得デリゲート（未指定時は <see cref="ILinkPreviewService"/> にフォールバック）。
    /// </summary>
    [Parameter] public Func<string, Task<LinkPreviewData?>>? LoadPreview { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        await ViewModel.UpdateParametersAsync(QuotedItemId, QuotedItem);
    }

    private Task<LinkPreviewData?> DefaultLoadPreview(string url) =>
        LinkPreviewService.GetPreviewAsync(url)!;

    private void HandleClick()
    {
        if (ViewModel.NavigationUrl is not null)
        {
            NavigationManager.NavigateTo(ViewModel.NavigationUrl);
        }
    }
}