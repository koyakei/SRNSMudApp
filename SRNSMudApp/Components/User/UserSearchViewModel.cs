namespace SRNSMudApp.Components.User;

using SRNSMudApp.Data;
using SRNSMudApp.Services;

/// <summary>
///     ユーザー検索 (UserSearch) 画面の状態管理および検索ロジックを担う ViewModel。
///     プライマリコンストラクタにより依存性注入の定型句を簡素化。
/// </summary>
public sealed class UserSearchViewModel(IUserDataProvider userDataProvider)
{
    public ApplicationUser? SelectedUser { get; set; }

    /// <summary>
    ///     正規化されたユーザー名でユーザーを非同期検索する。
    /// </summary>
    public async Task<IEnumerable<ApplicationUser>> SearchUsersAsync(string? query, CancellationToken token = default)
    {
        return await userDataProvider.SearchUsersByNormalizedNameAsync(query, token);
    }

    /// <summary>
    ///     選択されたユーザーをクリアする。
    /// </summary>
    public void ClearSelection()
    {
        SelectedUser = null;
    }

    /// <summary>
    ///     ユーザーを選択する。
    /// </summary>
    public void SelectUser(ApplicationUser? user)
    {
        SelectedUser = user;
    }
}