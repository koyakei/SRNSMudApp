namespace SRNSMudApp.Components.Tag;

using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;

using MudBlazor;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Services.Dialogs;

/// <summary>
///     アイテムに対するタグ付けリクエストチップ表示コンポーネントのコードビハインド。
///     リクエストの状態判定、タグ詳細ページへの遷移、およびスレッドダイアログの表示を制御する。
/// </summary>
public partial class ItemTagRequestChip : ComponentBase
{
    [Parameter]
    [EditorRequired]
    public TaggingRequestEntity Request { get; set; } = null!;

    [Parameter]
    [EditorRequired]
    public Item Item { get; set; } = null!;

    [Parameter]
    public EventCallback OnDataChanged { get; set; }

    [Inject]
    private IDialogLauncher DialogLauncher { get; set; } = null!;

    private bool _visible;
    private bool _isAdd;
    private int _replyCount;

    protected override void OnParametersSet()
    {
        // 可視性・種別・リプライ件数の計算は ViewModel に委譲する (mainRules: 条件文回避 / SoC)
        var state = ItemTagRequestChipViewModel.Compute(Request, Item);
        _visible = state.Visible;
        _isAdd = state.IsAdd;
        _replyCount = state.ReplyCount;
    }

    private async Task OpenThreadDialogAsync()
    {
        var parameters = new DialogParameters<TaggingRequestThreadDialog>
        {
            { x => x.TaggingRequest, Request }
        };

        var options = new DialogOptions
        {
            CloseButton = true,
            MaxWidth = MaxWidth.Small,
            FullWidth = true
        };

        var dialog = await DialogLauncher.ShowAsync<TaggingRequestThreadDialog>(
            $"Request: {Request.RequestedTag?.Name}",
            parameters,
            options);
        await dialog.Result;

        if (OnDataChanged.HasDelegate)
        {
            await OnDataChanged.InvokeAsync();
        }
    }
}