using System.ComponentModel.DataAnnotations;

namespace SRNSMudApp.Data;

/// <summary>
///     リプライ投稿時に通知対象として選択されたユーザーを記録するエンティティ。
///     Twitterのようにスレッド参加者（親アイテムオーナーや他リプライヤー）から個別に選択された宛先を永続化する。
/// </summary>
public class ItemReplyNotificationRecipient
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int ReplyItemId { get; set; }

    public Item ReplyItem { get; set; } = null!;

    [Required]
    [MaxLength(450)]
    public string RecipientUserId { get; set; } = string.Empty;

    public ApplicationUser RecipientUser { get; set; } = null!;

    public DateTimeOffset CreatedDate { get; set; } = DateTimeOffset.UtcNow;
}