#region

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

#endregion

namespace SRNSMudApp.Data;

public class Tag : BaseEntity
{
    [Required]
    [MaxLength(100)]
    [RegularExpression(@"^[\x20-\x7E\u3000-\u30FF\u4E00-\u9FFF\uFF01-\uFF9F]+$",
        ErrorMessage = "タグ名には漢字、ひらがな、カタカナ（半角/全角）、英数字（半角/全角）、アンダーバーのみ使用できます。")]
    public required string Name { get; set; }

    // DataType(DataType.Date) を削除
    public int CachedWeight { get; set; }

    public bool IsSystem { get; set; }

    // ReSharper disable once EntityFramework.ModelValidation.UnlimitedStringLength
    public string Content { get; set; } = "";

    [SuppressMessage("Usage", "CA2227:Collection properties should be read only")]
    public ICollection<TagRelation> TagRelations { get; set; } = [];

    // 親タグのID (null if root tag)
    public int? ParentTagId { get; set; }

    // 親タグへのナビゲーションプロパティ
    [ForeignKey("ParentTagId")] public Tag? ParentTag { get; set; }

    // 子タグのコレクション
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only")]
    public ICollection<Tag> Children { get; set; } = [];

    [SuppressMessage("Usage", "CA2227:Collection properties should be read only")]
    public ICollection<TagRelationToTag> TargetTagRelations { get; set; } = [];

    [SuppressMessage("Usage", "CA2227:Collection properties should be read only")]
    public ICollection<TagRelationToTag> SourceTagRelations { get; set; } = [];

    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public float[] Embedding { get; set; } = [];

    [SuppressMessage("Usage", "CA2227:Collection properties should be read only")]
    public ICollection<TagWeightLedger> TagWeightLedgers { get; set; } = [];
}