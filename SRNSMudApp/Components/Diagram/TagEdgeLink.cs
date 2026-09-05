using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;

using SRNSMudApp.Data;

namespace SRNSMudApp.Components.Diagram;

/// <summary>
///     Blazor.Diagrams 上で TagEdge を表すカスタムリンクモデル。
///     有向グラフの方向（始点から終点）を視覚的に表現する。
/// </summary>
public class TagEdgeLink : LinkModel
{
    /// <summary>
    ///     ポート円やノード枠線に隠れない、視認性の高い有向エッジ矢印マーカー。
    /// </summary>
    public static readonly LinkMarker DirectionArrow = new("M 0 -7 L 14 0 L 0 7 z", 16);

    public TagEdge Edge { get; set; }

    public TagEdgeLink(TagEdge edge, PortModel sourcePort, PortModel? targetPort = null)
        : base(sourcePort, targetPort)
    {
        ArgumentNullException.ThrowIfNull(edge);
        Edge = edge;
        Color = "#594ae2"; // MudBlazor Primary
        SelectedColor = "#ff4081"; // MudBlazor Secondary
        Width = 2.5;
        TargetMarker = DirectionArrow;
        UpdateLabels();
    }

    /// <summary>
    ///     紐付けられているタグ一覧をもとにリンクのラベル表示を更新する。
    /// </summary>
    public void UpdateLabels()
    {
        Labels.Clear();
        if (Edge.TagAttachments.Count > 0)
        {
            var text = string.Join(", ", Edge.TagAttachments.Select(a => a.Tag?.Name ?? $"Tag#{a.TagId}"));
            Labels.Add(new LinkLabelModel(this, text));
        }
    }
}