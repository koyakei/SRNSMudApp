namespace SRNSMudApp.Components.Diagram;

using System;
using System.Threading.Tasks;

using Blazor.Diagrams.Core.Models;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

using SRNSMudApp.Data;

/// <summary>
///     ダイアグラム上のタグノード表示ウィジェットコンポーネントのコードビハインド。
///     ノードのヘッダー表示、フォーカスロール（始点/終点/通常）、子タグ表示リクエスト、ツリーポップオーバー、ポートクラスの計算を制御する。
/// </summary>
public partial class TagNodeWidget : ComponentBase
{
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;

    /// <summary>
    ///     ウィジェットで描画対象となるタグノード。
    /// </summary>
    [Parameter] public TagNode Node { get; set; } = null!;

    private bool _isTreeOpen;

    private async Task ToggleTree()
    {
        _isTreeOpen = !_isTreeOpen;
        if (_isTreeOpen)
        {
            await Task.Yield();
            try
            {
                await JS.InvokeVoidAsync("contentOverflowHelper.scrollToElement", ".tag-tree-popover-content #tag-tree-current-line");
            }
            catch (JSException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private void HandleTagClick(int tagId)
    {
        _isTreeOpen = false;
        Node.RequestFocusTag?.Invoke(tagId);
    }

    private void HandleShowChildNodes()
    {
        Node.RequestShowChildNodes?.Invoke(Node.Tag);
    }

    private async Task HandleAddChildTag(Tag targetTag)
    {
        _isTreeOpen = false;
        if (Node.RequestAddChildTag != null)
        {
            await Node.RequestAddChildTag(targetTag);
        }
    }

    private void NavigateToTagDetail()
    {
        NavigationManager.NavigateTo($"/TagDetail/{Node.Tag.Id}");
    }

    private void HandleHideNode()
    {
        Node.RequestHideNode?.Invoke(Node.Tag);
    }

    private static string GetPortClass(PortAlignment alignment) => alignment switch
    {
        PortAlignment.Top => "port-top",
        PortAlignment.TopRight => "port-top-right",
        PortAlignment.Right => "port-right",
        PortAlignment.BottomRight => "port-bottom-right",
        PortAlignment.Bottom => "port-bottom",
        PortAlignment.BottomLeft => "port-bottom-left",
        PortAlignment.Left => "port-left",
        PortAlignment.TopLeft => "port-top-left",
        _ => string.Empty
    };
}