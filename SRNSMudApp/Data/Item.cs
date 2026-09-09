#region

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

#endregion

namespace SRNSMudApp.Data;

public class Item : BaseEntity, ITaggable
{
    public int TagTargetId { get; set; }
    public TaggableTarget TagTarget { get; set; } = null!;

    [DataType(DataType.MultilineText)] // Dateから修正
    [StringLength(1000, ErrorMessage = "{0}は{1}文字以内で入力してください。")]
    public string Content { get; set; } = string.Empty;

    // TagRelationを中間テーブルとして利用する場合
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only")]
    public ICollection<TagRelation> TagRelations { get; set; } = [];

    // JSON serialized ItemKind union (e.g. ReplyItem, RequestBodyItem, etc)
    public string ItemKindJson { get; set; } = string.Empty;

    // リプライ先のアイテム（親）
    public int? ParentItemId { get; set; }
    public Item? ParentItem { get; set; }

    // このアイテムに対するリプライ一覧
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only")]
    public ICollection<Item> Replies { get; set; } = [];

    // このアイテム（リプライ）の通知対象ユーザー一覧
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only")]
    public ICollection<ItemReplyNotificationRecipient> NotificationRecipients { get; set; } = [];

    // タグ付けリクエストに対するリプライの場合
    public int? TaggingRequestEntityId { get; set; }
    public TaggingRequestEntity? TaggingRequest { get; set; }

    // このアイテムがタグ付けリクエストの本体である場合、そのリクエスト情報
    public TaggingRequestEntity? AsRequestOf { get; set; }

    // 引用元のアイテム（引用リツイート先）
    public int? QuotedItemId { get; set; }
    public Item? QuotedItem { get; set; }

    // このアイテムを引用しているアイテム一覧
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only")]
    public ICollection<Item> QuotedByItems { get; set; } = [];

    /// <summary>プライベートモード（非公開）かどうか。</summary>
    public bool IsPrivate { get; set; }

    /// <summary>公開対象とするユーザーグループID（null の場合はフォロワー限定）。</summary>
    public int? TargetUserGroupId { get; set; }

    /// <summary>公開対象ユーザーグループへのナビゲーションプロパティ。</summary>
    public UserGroup? TargetUserGroup { get; set; }
}