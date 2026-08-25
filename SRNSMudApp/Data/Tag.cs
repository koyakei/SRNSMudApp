#region

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Models.Unions;

#endregion

namespace SRNSMudApp.Data;

public class Tag : BaseEntity
{
    public const string RootTagName = "全て∀";

    [Required]
    [MaxLength(100)]
    [RegularExpression(@"^[\x20-\x7E\u3000-\u30FF\u4E00-\u9FFF\uFF01-\uFF9F\u2200-\u22FF]+$",
        ErrorMessage = "タグ名には漢字、ひらがな、カタカナ（半角/全角）、英数字（半角/全角）、アンダーバー、数学記号のみ使用できます。")]
    public required string Name { get; set; }

    // DataType(DataType.Date) を削除
    public int CachedWeight { get; set; }

    public static readonly IReadOnlySet<string> VotingTagNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "good", "bad" };

    public bool IsSystem { get; set; }

    /// <summary>
    ///     タグの種別（VotingReactionTag / SystemClassificationTag / UserCustomTag）を取得する。
    /// </summary>
    public TagKind GetKind() =>
        VotingTagNames.Contains(Name)
            ? new VotingReactionTag(Name, OwnerId)
            : (OwnerId == "system" || IsSystem)
                ? new SystemClassificationTag(Name)
                : new UserCustomTag(Name, OwnerId);

    // ReSharper disable once EntityFramework.ModelValidation.UnlimitedStringLength
    public string Content { get; set; } = "";

    [SuppressMessage("Usage", "CA2227:Collection properties should be read only")]
    public ICollection<TagRelation> TagRelations { get; set; } = [];

    // hierarchyid 列。ルートタグは HierarchyId.GetRoot() で初期化
    public HierarchyId Node { get; set; } = HierarchyId.GetRoot();

    // 移行用に一時的に残す（データ移行完了後に削除予定）
    public int? ParentTagId { get; set; }

    [SuppressMessage("Usage", "CA2227:Collection properties should be read only")]
    public ICollection<TagRelationToTag> TargetTagRelations { get; set; } = [];

    [SuppressMessage("Usage", "CA2227:Collection properties should be read only")]
    public ICollection<TagRelationToTag> SourceTagRelations { get; set; } = [];

    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public float[] Embedding { get; set; } = [];

    [SuppressMessage("Usage", "CA2227:Collection properties should be read only")]
    public ICollection<TagWeightLedger> TagWeightLedgers { get; set; } = [];
}