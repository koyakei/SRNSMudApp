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

    public static readonly IReadOnlySet<string> VoteTagNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "good", "bad" };

    public static readonly IReadOnlySet<string> ReactionTagNames =
        new HashSet<string>(SRNSMudApp.Models.ReactionTagNames.All, StringComparer.OrdinalIgnoreCase);

    public bool IsSystem { get; set; }

    /// <summary>
    ///     このタグが明示的にロックされているかどうか。
    ///     ロックされたタグおよびその兄弟タグは作成・編集・削除が不能となる。
    /// </summary>
    public bool IsLocked { get; set; }

    /// <summary>
    ///     このタグへのタグ付けリクエストを自動承認するかどうか。
    /// </summary>
    public bool AutoAcceptIncomingTaggingRequests { get; set; }

    /// <summary>
    ///     自動承認を委任するユーザーグループの ID。
    ///     設定されている場合、このグループに属するメンバーからのリクエストが自動承認される。
    /// </summary>
    [Obsolete("後方互換のために保持しています。新規コードでは AutoApproveUserGroups を使用してください。")]
    public int? AutoApproveUserGroupId { get; set; }

    [Obsolete("後方互換のために保持しています。新規コードでは AutoApproveUserGroups を使用してください。")]
    public UserGroup? AutoApproveUserGroup { get; set; }

    /// <summary>
    ///     自動承認を許可するユーザーグループのコレクション。
    /// </summary>
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only")]
    public ICollection<TagAutoApproveUserGroup> AutoApproveUserGroups { get; set; } = [];

    /// <summary>
    ///     タグの種別（VoteTag / ReactionTag / SystemClassificationTag / UserCustomTag）を取得する。
    /// </summary>
    public TagKind GetKind() =>
        VoteTagNames.Contains(Name)
            ? new VoteTag(Name, OwnerId)
            : ReactionTagNames.Contains(Name)
                ? new ReactionTag(Name, OwnerId)
                : (OwnerId == "system" || IsSystem)
                    ? new SystemClassificationTag(Name)
                    : new UserCustomTag(Name, OwnerId);

    /// <summary>
    ///     タグの CachedWeight に指定された差分（delta）を適用し、更新前の値と更新後の値をタプルで返す。
    /// </summary>
    /// <param name="delta">増減値。</param>
    /// <returns>更新前の CachedWeight と更新後の CachedWeight のタプル。</returns>
    public (int PreviousWeight, int NewWeight) ApplyWeightDelta(int delta)
    {
        int prevWeight = CachedWeight;
        CachedWeight += delta;
        return (prevWeight, CachedWeight);
    }

    // ReSharper disable once EntityFramework.ModelValidation.UnlimitedStringLength
    public string Content { get; set; } = "";

    [SuppressMessage("Usage", "CA2227:Collection properties should be read only")]
    public ICollection<TagRelation> TagRelations { get; set; } = [];

    // hierarchyid 列。ルートタグ以外で未指定の場合は SaveChanges で自動的にルートタグ配下に配置
    public HierarchyId Node { get; set; } = null!;

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

    /// <summary>プライベートモード（非公開）かどうか。</summary>
    public bool IsPrivate { get; set; }

    /// <summary>公開対象とするユーザーグループID（null の場合はフォロワー限定）。</summary>
    public int? TargetUserGroupId { get; set; }

    /// <summary>公開対象ユーザーグループへのナビゲーションプロパティ。</summary>
    public UserGroup? TargetUserGroup { get; set; }

    /// <summary>管理者によって強制的に非公開化されたかどうか。</summary>
    public bool IsAdminHidden { get; set; }

    /// <summary>管理者によって非公開化された日時（UTC）。</summary>
    public DateTimeOffset? AdminHiddenAt { get; set; }

    /// <summary>管理者による非公開化の理由。</summary>
    public string? AdminHiddenReason { get; set; }
}