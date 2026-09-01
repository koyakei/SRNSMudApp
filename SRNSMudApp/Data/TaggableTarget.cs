using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace SRNSMudApp.Data;

/// <summary>
///     タグ付けやタグ付けリクエストの対象となり得るエンティティの共通アンカー。
///     Item や TagEdge と 1:1 で紐付き、TaggingRequestContracts からの一意な参照先となる。
/// </summary>
public class TaggableTarget : BaseEntity
{
    [MaxLength(50)]
    public string TargetType { get; set; } = string.Empty; // "Item", "TagEdge"

    // 1:1 ナビゲーションプロパティ
    public Item? Item { get; set; }
    public TagEdge? TagEdge { get; set; }

    // この対象に対するタグ付けリクエスト一覧
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only")]
    public ICollection<TaggingRequestEntity> TaggingRequests { get; set; } = [];
}