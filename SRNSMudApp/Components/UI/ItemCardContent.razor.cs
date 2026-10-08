namespace SRNSMudApp.Components.UI;

using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

using SRNSMudApp.Models;
using SRNSMudApp.Services;

/// <summary>
///     アイテム本文の表示専用子コンポーネントのコードビハインド。
///     本文の展開/折りたたみ・URL プレビュー・投稿者/最終更新メタ情報を担い、
///     オーバーフロー検知の JS インターロップをここに集約する。
/// </summary>
public partial class ItemCardContent : ComponentBase, IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = null!;

    [Parameter] public int ItemId { get; set; }
    [Parameter] public string? Content { get; set; }
    [Parameter] public string? OwnerId { get; set; }
    [Parameter] public string? OwnerUserName { get; set; }
    [Parameter] public DateTime UpdatedDate { get; set; }
    [Parameter] public bool IsPrivate { get; set; }
    [Parameter] public bool IsAdminHidden { get; set; }
    [Parameter] public string? TargetUserGroupName { get; set; }
    [Parameter] public bool EnableNavigation { get; set; } = true;

    /// <summary>リンクプレビュー取得デリゲート（UrlPreviewCard へ委譲）。</summary>
    [Parameter] public Func<string, Task<LinkPreviewData?>>? LoadPreview { get; set; }

    private string DisplayContent => string.IsNullOrWhiteSpace(Content) ? "内容なし" : Content;

    private bool _isExpanded;
    private bool _isOverflowing;

    private IReadOnlyList<ContentSegment> _segments = [];

    private DotNetObjectReference<ItemCardContent>? _dotNetRef;
    private bool _needsOverflowCheck = true;

    protected override void OnParametersSet()
    {
        _segments = ItemCardViewModel.GetContentSegments(DisplayContent);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await (firstRender switch
        {
            true => InitializeOverflowHelperAsync(),
            false => Task.CompletedTask
        });

        await (_needsOverflowCheck switch
        {
            true => CheckOverflowNowAsync(),
            false => Task.CompletedTask
        });
    }

    /// <summary>オーバーフロー監視 (resize 通知) のための DotNetObjectReference を登録する。</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "JS インターロップの初期化失敗は UI 描画に致命的でないため握りつぶす")]
    private async Task InitializeOverflowHelperAsync()
    {
        _dotNetRef = DotNetObjectReference.Create(this);
        try
        {
            await JS.InvokeVoidAsync("contentOverflowHelper.init", _dotNetRef);
        }
        catch (Exception)
        {
            // ignored
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "要素監視登録の失敗は UI 描画に致命的でないため握りつぶす")]
    private async Task CheckOverflowNowAsync()
    {
        _needsOverflowCheck = false;
        await UpdateOverflowStateAsync();
        try
        {
            await JS.InvokeVoidAsync("contentOverflowHelper.observeElements", $"#item-card-{ItemId}");
        }
        catch (Exception)
        {
            // ignored
        }
    }

    /// <summary>resize 時にオーバーフロー状態を再計算するためのコールバック。</summary>
    [JSInvokable]
    public void OnWindowResized()
    {
        _needsOverflowCheck = true;
        InvokeAsync(StateHasChanged);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "オーバーフロー判定失敗は UI 描画に致命的でないため握りつぶす")]
    private async Task UpdateOverflowStateAsync()
    {
        if (_isExpanded)
        {
            return;
        }

        try
        {
            var overflowIds = await JS.InvokeAsync<int[]>("contentOverflowHelper.checkOverflow", [ItemId]);
            var isOverflowing = overflowIds.Contains(ItemId);
            if (_isOverflowing != isOverflowing)
            {
                _isOverflowing = isOverflowing;
                StateHasChanged();
            }
        }
        catch (Exception)
        {
            // ignored
        }
    }

    private void ToggleExpand()
    {
        _isExpanded = !_isExpanded;
        if (!_isExpanded)
        {
            _needsOverflowCheck = true; // 折りたたみ時に再チェック
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "非同期破棄時の JS オブジェクト参照破棄エラーは無視する")]
    public async ValueTask DisposeAsync()
    {
        if (_dotNetRef is not null)
        {
            try
            {
                await JS.InvokeVoidAsync("contentOverflowHelper.removeDotNetRef", _dotNetRef);
            }
            catch (Exception)
            {
                // ignored
            }
            _dotNetRef.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}