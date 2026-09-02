using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;

using TagEntity = SRNSMudApp.Data.Tag;

namespace SRNSMudApp.Components.Diagram;

/// <summary>
///     ダイアグラム上でのタグフォーカスの役割（単一、始点候補、終点候補）。
/// </summary>
public enum TagFocusRole
{
    None = 0,
    Source = 1,
    Target = 2
}

/// <summary>
///     Blazor.Diagrams 上でタグを表すカスタムノードモデル。
/// </summary>
public class TagNode : NodeModel
{
    public TagEntity Tag { get; }
    public TagFocusRole FocusRole { get; set; } = TagFocusRole.None;

    public bool IsFocused
    {
        get => FocusRole != TagFocusRole.None;
        set => FocusRole = value ? (FocusRole == TagFocusRole.None ? TagFocusRole.Source : FocusRole) : TagFocusRole.None;
    }

    public IReadOnlyList<TagEntity> AllTags { get; set; } = [];

    /// <summary>
    ///     ツリーポップオーバー等から特定タグへのフォーカス移動を要求するコールバック。
    /// </summary>
    public Action<int>? RequestFocusTag { get; set; }

    /// <summary>
    ///     ツリーポップオーバー等から子タグ追加を要求するコールバック。
    /// </summary>
    public Func<TagEntity, Task>? RequestAddChildTag { get; set; }

    public TagNode(TagEntity tag, Point? position = null) : base(position)
    {
        Tag = tag ?? throw new ArgumentNullException(nameof(tag));
        _ = AddPort(PortAlignment.Left);
        _ = AddPort(PortAlignment.Right);
        _ = AddPort(PortAlignment.Top);
        _ = AddPort(PortAlignment.Bottom);
    }
}