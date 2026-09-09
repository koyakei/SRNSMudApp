// Data/UserFollow.cs
namespace SRNSMudApp.Data;

/// <summary>
///     ユーザー間のフォロー関係を表すエンティティ。
///     BaseEntity を継承し、OwnerId がフォローを実行したユーザー（フォロワー）、
///     FollowedUserId がフォローされた対象ユーザーを表す。
/// </summary>
public class UserFollow : BaseEntity
{
    /// <summary>
    ///     フォロー対象のユーザーID。
    /// </summary>
    public required string FollowedUserId { get; set; }

    /// <summary>
    ///     フォロー対象ユーザーへのナビゲーションプロパティ。
    /// </summary>
    public ApplicationUser FollowedUser { get; set; } = null!;
}