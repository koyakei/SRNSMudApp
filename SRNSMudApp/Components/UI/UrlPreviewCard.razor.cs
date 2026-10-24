namespace SRNSMudApp.Components.UI;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

using SRNSMudApp.Models;

/// <summary>
///     リンクプレビューの表示専用子コンポーネントのコードビハインド。
///     プレビューデータの取得は親から注入される LoadPreview デリゲート経由で実行し、
///     AsyncPageState による状態遷移管理を行う。
/// </summary>
public partial class UrlPreviewCard
{
    [Parameter]
    [EditorRequired]
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "Blazor component parameter")]
    public string Url { get; set; } = null!;

    /// <summary>プレビューデータ取得デリゲート（親から注入。null の場合は取得しない）。</summary>
    [Parameter]
    public Func<string, Task<LinkPreviewData?>>? LoadPreview { get; set; }

    [Inject]
    private NavigationManager NavigationManager { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    // 取得状態は bool フラグではなく union で表現する (mainRules: 再代入撲滅 / if文撲滅)
    private AsyncPageState<LinkPreviewData> _state = new Loading();
    private string? _selectedText;
    private bool _imageLoadFailed;

    protected override async Task OnParametersSetAsync()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            return;
        }

        _selectedText = UrlPreviewCardViewModel.ExtractTextFragment(Url);

        if (LoadPreview is null)
        {
            _state = new Empty("プレビュー取得デリゲート未設定");
            return;
        }

        _state = new Loading();
        LinkPreviewData? preview = await LoadPreview(Url);

        _state = preview is { IsSuccess: true }
            ? new Loaded<LinkPreviewData>(preview)
            : new Empty("プレビューを取得できませんでした");
    }

    private void HandleImageError()
    {
        _imageLoadFailed = true;
    }

    private async Task NavigateToUrl()
    {
        if (string.IsNullOrEmpty(Url))
        {
            return;
        }

        // 内部リンク（相対パスまたは同一オリジンのパス）の場合は SPA 内で画面遷移する
        if (UrlPreviewCardViewModel.TryGetInternalRoute(Url, NavigationManager.BaseUri, out var internalRoute))
        {
            NavigationManager.NavigateTo(internalRoute);
            return;
        }

        try
        {
            await JS.InvokeVoidAsync("open", Url, "_blank");
        }
        catch (JSException)
        {
            // ignored
        }
    }
}