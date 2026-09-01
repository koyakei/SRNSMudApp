namespace SRNSMudApp.Data;

/// <summary>
///     タグ付け対象（TaggableTarget）を持ち、タグ付けリクエストの対象となり得るエンティティのインターフェース。
/// </summary>
public interface ITaggable : IOwnable
{
    int Id { get; set; }
    int TagTargetId { get; set; }
    TaggableTarget TagTarget { get; set; }
}

/// <summary>
///     直接的な Tags コレクションを持つエンティティ用のインターフェース（後方互換性）。
/// </summary>
public interface IDirectTaggable
{
    int Id { get; set; }
    ICollection<Tag> Tags { get; }
}