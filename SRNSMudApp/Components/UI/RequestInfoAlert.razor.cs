namespace SRNSMudApp.Components.UI;

using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;

using SRNSMudApp.Data;

/// <summary>
///     タグ付けリクエスト情報のアラート表示コンポーネントのコードビハインド。
///     承認・却下・取り下げ等のアクション発火、および対象コンテンツの展開・折りたたみを管理する。
/// </summary>
public partial class RequestInfoAlert : ComponentBase
{
    [Parameter]
    [EditorRequired]
    public RequestInfo RequestInfo { get; set; } = null!;

    [Parameter]
    public bool CanCancel { get; set; }

    [Parameter]
    public bool CanApprove { get; set; }

    [Parameter]
    public bool CanReject { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    [Parameter]
    public EventCallback OnApprove { get; set; }

    [Parameter]
    public EventCallback OnReject { get; set; }

    private bool _isTargetExpanded;

    private void ToggleTargetExpand()
    {
        _isTargetExpanded = !_isTargetExpanded;
    }

    private async Task OnCancelClicked()
    {
        if (OnCancel.HasDelegate)
        {
            await OnCancel.InvokeAsync();
        }
    }

    private async Task OnApproveClicked()
    {
        if (OnApprove.HasDelegate)
        {
            await OnApprove.InvokeAsync();
        }
    }

    private async Task OnRejectClicked()
    {
        if (OnReject.HasDelegate)
        {
            await OnReject.InvokeAsync();
        }
    }
}