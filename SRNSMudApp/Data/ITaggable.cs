namespace SRNSMudApp.Data;

/// <summary>
///     タグ付け対象（TaggableTarget）を持ち、タグ付けリクエストの対象となり得るエンティティのインターフェース。
/// </summary>
public interface ITaggable : IOwnable
{
    /// <summary>
    ///     エンティティの識別子。
    /// </summary>
    int Id { get; set; }

    /// <summary>
    ///     紐付く <see cref="TaggableTarget" /> の ID。
    /// </summary>
    int TagTargetId { get; set; }

    /// <summary>
    ///     紐付く <see cref="TaggableTarget" /> のナビゲーションプロパティ。
    /// </summary>
    TaggableTarget TagTarget { get; set; }
}

/// <summary>
///     直接的な Tags コレクションを持つエンティティ用のインターフェース（後方互換性）。
/// </summary>
public interface IDirectTaggable
{
    /// <summary>
    ///     エンティティの識別子。
    /// </summary>
    int Id { get; set; }

    /// <summary>
    ///     直接紐付くタグのコレクション。
    /// </summary>
    ICollection<Tag> Tags { get; }
}