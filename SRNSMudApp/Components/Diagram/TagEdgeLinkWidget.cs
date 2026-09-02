using System.Globalization;

using Blazor.Diagrams.Components;

using Microsoft.AspNetCore.Components.Rendering;

namespace SRNSMudApp.Components.Diagram;

/// <summary>
///     TagEdgeLink 用のカスタムリンクコンポーネント。
///     エッジの 1/3 と 2/3 の位置に、エッジ線の太さの 5 倍の幅を持つ方向矢印を描画する。
/// </summary>
public class TagEdgeLinkWidget : LinkWidget
{
#pragma warning disable IDE1006 // LinkWidget.BuildRenderTree の引数名 __builder に一致させるため
    protected override void BuildRenderTree(RenderTreeBuilder __builder)
#pragma warning restore IDE1006
    {
        base.BuildRenderTree(__builder);

        if (Link is not TagEdgeLink || Link.PathGeneratorResult?.FullPath is not { } sp || sp.Length <= 0)
        {
            return;
        }

        // 1/3 位置と 2/3 位置の座標および進行方向の接線（Tangent）を取得
        var p13 = sp.GetPropertiesAtLength(sp.Length / 3.0);
        double angle13 = Math.Atan2(p13.TangentY, p13.TangentX) * 180.0 / Math.PI;

        var p23 = sp.GetPropertiesAtLength(sp.Length * 2.0 / 3.0);
        double angle23 = Math.Atan2(p23.TangentY, p23.TangentX) * 180.0 / Math.PI;

        // エッジの線の太さの 5 倍の幅の矢印
        double arrowWidth = Link.Width * 5.0;
        double halfWidth = arrowWidth / 2.0;
        double halfHeight = arrowWidth * 0.35;

        string arrowPath = string.Create(
            CultureInfo.InvariantCulture,
            $"M {-halfWidth:F2} {-halfHeight:F2} L {halfWidth:F2} 0 L {-halfWidth:F2} {halfHeight:F2} z");
        string color = (Link.Selected ? Link.SelectedColor : Link.Color) ?? "#594ae2";

        __builder.OpenElement(100, "g");
        __builder.AddAttribute(101, "class", "diagram-link-intermediate-arrows");
        __builder.AddAttribute(102, "pointer-events", "none");

        // 1/3 位置の矢印
        __builder.OpenElement(103, "g");
        __builder.AddAttribute(104, "transform", string.Create(
            CultureInfo.InvariantCulture,
            $"translate({p13.X:F2}, {p13.Y:F2}) rotate({angle13:F2})"));
        __builder.OpenElement(105, "path");
        __builder.AddAttribute(106, "d", arrowPath);
        __builder.AddAttribute(107, "fill", color);
        __builder.CloseElement();
        __builder.CloseElement();

        // 2/3 位置の矢印
        __builder.OpenElement(108, "g");
        __builder.AddAttribute(109, "transform", string.Create(
            CultureInfo.InvariantCulture,
            $"translate({p23.X:F2}, {p23.Y:F2}) rotate({angle23:F2})"));
        __builder.OpenElement(110, "path");
        __builder.AddAttribute(111, "d", arrowPath);
        __builder.AddAttribute(112, "fill", color);
        __builder.CloseElement();
        __builder.CloseElement();

        __builder.CloseElement();
    }
}