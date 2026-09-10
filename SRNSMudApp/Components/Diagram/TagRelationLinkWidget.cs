using System.Globalization;

using Blazor.Diagrams.Components;

using Microsoft.AspNetCore.Components.Rendering;

using MudBlazor;

namespace SRNSMudApp.Components.Diagram;

/// <summary>
///     TagRelationLink 用のカスタムリンクコンポーネント。
///     エッジの中央に @Icons.Material.Filled.Sell のアイコンを描画する。
/// </summary>
public class TagRelationLinkWidget : LinkWidget
{
#pragma warning disable IDE1006 // LinkWidget.BuildRenderTree の引数名 __builder に一致させるため
    protected override void BuildRenderTree(RenderTreeBuilder __builder)
#pragma warning restore IDE1006
    {
        base.BuildRenderTree(__builder);

        if (Link is not TagRelationLink || Link.PathGeneratorResult?.FullPath is not { } sp || sp.Length <= 0)
        {
            return;
        }

        // 中央位置の座標を取得
        var center = sp.GetPropertiesAtLength(sp.Length / 2.0);
        double angle = Math.Atan2(center.TangentY, center.TangentX) * 180.0 / Math.PI;

        string color = (Link.Selected ? Link.SelectedColor : Link.Color) ?? "#594ae2";

        __builder.OpenElement(100, "g");
        __builder.AddAttribute(101, "class", "diagram-link-sell-icon");
        __builder.AddAttribute(102, "pointer-events", "none");

        __builder.OpenElement(103, "g");
        // アイコンの中心を合わせるために -12, -12 する (24x24のアイコンを想定)
        __builder.AddAttribute(104, "transform", string.Create(
            CultureInfo.InvariantCulture,
            $"translate({center.X:F2}, {center.Y:F2}) rotate({angle:F2}) translate(-12, -12)"));
        __builder.OpenElement(105, "path");
        __builder.AddAttribute(106, "d", Icons.Material.Filled.Sell);
        __builder.AddAttribute(107, "fill", color);
        __builder.CloseElement();
        __builder.CloseElement();

        __builder.CloseElement();
    }
}