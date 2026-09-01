using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;

using SRNSMudApp.Data;

namespace SRNSMudApp.Components.Diagram;

/// <summary>
///     Blazor.Diagrams 上で TagEdge を表すカスタムリンクモデル。
/// </summary>
public class TagEdgeLink : LinkModel
{
    public TagEdge Edge { get; set; }

    public TagEdgeLink(TagEdge edge, PortModel sourcePort, PortModel? targetPort = null)
        : base(sourcePort, targetPort)
    {
        Edge = edge ?? throw new ArgumentNullException(nameof(edge));
        TargetMarker = LinkMarker.Arrow;
        UpdateLabels();
    }

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