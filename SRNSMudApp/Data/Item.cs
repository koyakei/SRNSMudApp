#region

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

#endregion

namespace SRNSMudApp.Data;

public class Item : BaseEntity
{
    [DataType(DataType.MultilineText)] // Dateから修正
    [StringLength(1000, ErrorMessage = "{0}は{1}文字以内で入力してください。")]
    public string Content { get; set; } = string.Empty;

    // TagRelationを中間テーブルとして利用する場合
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only")]
    public ICollection<TagRelation> TagRelations { get; set; } = [];

    // リプライ先のアイテム（親）
    public int? ParentItemId { get; set; }
    public Item? ParentItem { get; set; }

    // このアイテムに対するリプライ一覧
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only")]
    public ICollection<Item> Replies { get; set; } = [];
}