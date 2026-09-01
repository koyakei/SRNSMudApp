using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;

using TagEntity = SRNSMudApp.Data.Tag;

namespace SRNSMudApp.Components.Diagram;

/// <summary>
///     Blazor.Diagrams 上でタグを表すカスタムノードモデル。
/// </summary>
public class TagNode : NodeModel
{
    public TagEntity Tag { get; }
    public bool IsFocused { get; set; }
    public IReadOnlyList<TagEntity> AllTags { get; set; } = [];

    /// <summary>
    ///     ツリーポップオーバー等から特定タグへのフォーカス移動を要求するコールバック。
    /// </summary>
    public Action<int>? RequestFocusTag { get; set; }

    public TagNode(TagEntity tag, Point? position = null) : base(position)
    {
        Tag = tag ?? throw new ArgumentNullException(nameof(tag));
        _ = AddPort(PortAlignment.Left);
        _ = AddPort(PortAlignment.Right);
        _ = AddPort(PortAlignment.Top);
        _ = AddPort(PortAlignment.Bottom);
    }
}