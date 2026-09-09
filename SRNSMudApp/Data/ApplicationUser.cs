#region

using Microsoft.AspNetCore.Identity;

#endregion

namespace SRNSMudApp.Data;

// Add profile data for application users by adding properties to the ApplicationUser class
public class ApplicationUser : IdentityUser
{
    /// <summary>
    ///     アカウントのデフォルトプライベートモード設定。
    ///     true の場合、新規投稿時にデフォルトでプライベートモードが有効化される。
    /// </summary>
    public bool IsPrivateModeDefault { get; set; }

    /// <summary>
    ///     デフォルトの公開対象ユーザーグループID（null の場合はフォロワー限定）。
    /// </summary>
    public int? DefaultPrivateUserGroupId { get; set; }

    /// <summary>
    ///     デフォルト公開対象ユーザーグループへのナビゲーションプロパティ。
    /// </summary>
    public UserGroup? DefaultPrivateUserGroup { get; set; }
}