using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace SRNSMudApp.Data;

/// <summary>
///     タグ付けやタグ付けリクエストの対象となり得るエンティティの共通アンカー。
///     Item や TagEdge と 1:1 で紐付き、TaggingRequestContracts からの一意な参照先となる。
/// </summary>
public class TaggableTarget : BaseEntity
{
    /// <summary>
    ///     タグ付け対象のエンティティ種別（"Item", "TagEdge" など）。
    /// </summary>
    [MaxLength(50)]
    public string TargetType { get; set; } = string.Empty;

    /// <summary>
    ///     対象が <see cref="Item" /> の場合のナビゲーションプロパティ。
    /// </summary>
    public Item? Item { get; set; }

    /// <summary>
    ///     対象が <see cref="TagEdge" /> の場合のナビゲーションプロパティ。
    /// </summary>
    public TagEdge? TagEdge { get; set; }

    /// <summary>
    ///     この対象に対するタグ付けリクエスト一覧。
    /// </summary>
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only")]
    public ICollection<TaggingRequestEntity> TaggingRequests { get; set; } = [];
}