using System.Diagnostics.CodeAnalysis;

namespace SRNSMudApp.Data;

/// <summary>
///     タグとタグの間の有向関係（Edge）を表すエンティティ。
///     Edge 自体が何を意味するかは <see cref="TagEdgeTagAttachment" /> を通じて
///     複数の Tag を紐付けることで定義する。
/// </summary>
public class TagEdge : BaseEntity, ITaggable
{
    public int TagTargetId { get; set; }
    public TaggableTarget TagTarget { get; set; } = null!;

    public int SourceTagId { get; set; }
    public Tag SourceTag { get; set; } = null!;

    public int TargetTagId { get; set; }
    public Tag TargetTag { get; set; } = null!;

    [SuppressMessage("Usage", "CA2227:Collection properties should be read only")]
    public ICollection<TagEdgeTagAttachment> TagAttachments { get; set; } = [];
}