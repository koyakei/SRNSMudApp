using System.Diagnostics.CodeAnalysis;

namespace SRNSMudApp.Data;

/// <summary>
///     タグとタグの間の有向関係（Edge）を表すエンティティ。
///     Edge 自体が何を意味するかは <see cref="TagEdgeTagAttachment" /> を通じて
///     複数の Tag を紐付けることで定義する。
/// </summary>
public class TagEdge : BaseEntity, ITaggable
{
    /// <summary>
    ///     紐付く <see cref="TaggableTarget" /> の ID。
    /// </summary>
    public int TagTargetId { get; set; }

    /// <summary>
    ///     紐付く <see cref="TaggableTarget" /> のナビゲーションプロパティ。
    /// </summary>
    public TaggableTarget TagTarget { get; set; } = null!;

    /// <summary>
    ///     エッジの始点となるタグの ID。
    /// </summary>
    public int SourceTagId { get; set; }

    /// <summary>
    ///     エッジの始点となるタグ。
    /// </summary>
    public Tag SourceTag { get; set; } = null!;

    /// <summary>
    ///     エッジの終点となるタグの ID。
    /// </summary>
    public int TargetTagId { get; set; }

    /// <summary>
    ///     エッジの終点となるタグ。
    /// </summary>
    public Tag TargetTag { get; set; } = null!;

    /// <summary>
    ///     このエッジに付与された意味付けタグのアタッチメント一覧。
    /// </summary>
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only")]
    public ICollection<TagEdgeTagAttachment> TagAttachments { get; set; } = [];
}