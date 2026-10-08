namespace SRNSMudApp.Components.Tag;

using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;

using MudBlazor;

/// <summary>
///     アクションタグリクエストチップ表示コンポーネントのコードビハインド。
///     承認・キャンセルボタン、タグ名、オーナー名、Weight表示および各イベントハンドリングを制御する。
/// </summary>
public partial class ActionTagRequestChip : ComponentBase
{
    /// <summary>タグ名</summary>
    [Parameter]
    public string TagName { get; set; } = string.Empty;

    /// <summary>リンク先URL</summary>
    [Parameter]
    public string? Href { get; set; }

    /// <summary>タグの所有者名</summary>
    [Parameter]
    public string? OwnerName { get; set; }

    /// <summary>Weight</summary>
    [Parameter]
    public int Weight { get; set; }

    /// <summary>
    ///     削除リクエストを承認できる権限があるかどうか
    /// </summary>
    [Parameter]
    public bool CanApprove { get; set; }

    /// <summary>
    ///     削除リクエストをキャンセルできる権限があるかどうか（リクエストした本人など）
    /// </summary>
    [Parameter]
    public bool CanCancel { get; set; }

    /// <summary>承認時のコールバック</summary>
    [Parameter]
    public EventCallback OnApprove { get; set; }

    /// <summary>キャンセル時のコールバック</summary>
    [Parameter]
    public EventCallback OnCancel { get; set; }

    /// <summary>タグクリック時のコールバック</summary>
    [Parameter]
    public EventCallback OnTagClick { get; set; }

    [Parameter]
    public string? Style { get; set; }

    [Parameter]
    public string? TagStyle { get; set; }

    /// <summary>削除リクエスト中を表現するためにデフォルトはWarning</summary>
    [Parameter]
    public Color Color { get; set; } = Color.Warning;

    [Parameter]
    public Variant Variant { get; set; } = Variant.Outlined;

    private async Task OnApproveClicked()
    {
        if (OnApprove.HasDelegate)
        {
            await OnApprove.InvokeAsync();
        }
    }

    private async Task OnCancelClicked()
    {
        if (OnCancel.HasDelegate)
        {
            await OnCancel.InvokeAsync();
        }
    }

    private async Task HandleTagClick()
    {
        if (OnTagClick.HasDelegate)
        {
            await OnTagClick.InvokeAsync();
        }
    }
}