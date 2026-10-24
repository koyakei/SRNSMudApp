namespace SRNSMudApp.Services;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Data;

/// <summary>
/// タグダイアグラム関連エンティティを API 向け DTO に変換するファクトリ / マッパーインターフェース。
/// </summary>
public interface ITagDiagramDtoMapper
{
    /// <summary>
    /// Tag エンティティを TagDto に変換します。
    /// </summary>
    /// <param name="tag">変換元のタグエンティティ。</param>
    /// <returns>変換後の TagDto。</returns>
    TagDto MapToTagDto(Tag tag);

    /// <summary>
    /// Item エンティティを ItemDto に変換します。
    /// </summary>
    /// <param name="item">変換元のアイテムエンティティ。</param>
    /// <returns>変換後の ItemDto。</returns>
    ItemDto MapToItemDto(Item item);

    /// <summary>
    /// TagEdgeTagAttachment エンティティを TagEdgeAttachmentDto に変換します。
    /// </summary>
    /// <param name="att">変換元の意味付けタグ関連付けエンティティ。</param>
    /// <returns>変換後の TagEdgeAttachmentDto。</returns>
    TagEdgeAttachmentDto MapToAttachmentDto(TagEdgeTagAttachment att);
}