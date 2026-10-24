#region

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

using UserGroupEntity = SRNSMudApp.Data.UserGroup;

#endregion

namespace SRNSMudApp.Components.UserGroup;

/// <summary>
///     ユーザーグループメンバー管理ダイアログ用 ViewModel。
///     メンバー一覧の取得・整列、新規メンバー検索、メンバー追加・削除処理をカプセル化する。
/// </summary>
public sealed class UserGroupMembersViewModel
{
    private readonly IUserGroupDataProvider _userGroupDataProvider;
    private readonly IUserDataProvider _userDataProvider;

    public UserGroupMembersViewModel(
        IUserGroupDataProvider userGroupDataProvider,
        IUserDataProvider userDataProvider)
    {
        _userGroupDataProvider = userGroupDataProvider ?? throw new ArgumentNullException(nameof(userGroupDataProvider));
        _userDataProvider = userDataProvider ?? throw new ArgumentNullException(nameof(userDataProvider));
    }

    public UserGroupEntity? Group { get; private set; }
    public string CurrentUserId { get; private set; } = string.Empty;
    public IReadOnlyList<UserGroupMember> Members { get; private set; } = [];
    public ApplicationUser? SelectedUser { get; set; }
    public bool IsLoading { get; private set; } = true;
    public bool IsAdding { get; private set; }
    public bool IsRemoving { get; private set; }

    public bool IsOwner => Group != null && Group.OwnerId == CurrentUserId;
    public bool CanAddMember => SelectedUser != null && !IsAdding && !IsLoading && IsOwner;

    /// <summary>
    ///     ダイアログ初期化パラメータを設定し、メンバー一覧を読み込む。
    /// </summary>
    public async Task InitializeAsync(
        UserGroupEntity group,
        string currentUserId,
        CancellationToken cancellationToken = default)
    {
        Group = group ?? throw new ArgumentNullException(nameof(group));
        CurrentUserId = currentUserId ?? string.Empty;
        SelectedUser = null;
        IsAdding = false;
        IsRemoving = false;

        await LoadMembersAsync(cancellationToken);
    }

    /// <summary>
    ///     グループIDからグループ情報を読み込み、メンバー一覧を初期化する。
    /// </summary>
    public async Task InitializeAsync(
        int groupId,
        string currentUserId,
        CancellationToken cancellationToken = default)
    {
        CurrentUserId = currentUserId ?? string.Empty;
        SelectedUser = null;
        IsAdding = false;
        IsRemoving = false;

        IsLoading = true;
        try
        {
            UserGroupEntity? group = await _userGroupDataProvider.GetUserGroupByIdAsync(groupId, cancellationToken);
            Group = group;
            if (group is not null)
            {
                Members = group.Members
                    .OrderBy(m => m.UserId == group.OwnerId ? 0 : 1)
                    .ThenBy(m => m.CreatedDate)
                    .ToList();
            }
            else
            {
                Members = [];
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    ///     最新のグループ情報を取得し、メンバー一覧をソートして保持する。
    /// </summary>
    public async Task LoadMembersAsync(CancellationToken cancellationToken = default)
    {
        if (Group == null)
        {
            return;
        }

        IsLoading = true;
        try
        {
            UserGroupEntity? group = await _userGroupDataProvider.GetUserGroupByIdAsync(Group.Id, cancellationToken);
            if (group is not null)
            {
                Members = group.Members
                    .OrderBy(m => m.UserId == Group.OwnerId ? 0 : 1)
                    .ThenBy(m => m.CreatedDate)
                    .ToList();
            }
            else
            {
                Members = [];
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    ///     新規追加対象のユーザーを検索する（既存メンバーは除外）。
    /// </summary>
    public async Task<IEnumerable<ApplicationUser>> SearchUsersAsync(
        string? value,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        List<ApplicationUser> users = await _userDataProvider.SearchUsersAsync(value, cancellationToken);
        var existingUserIds = Members.Select(m => m.UserId).ToHashSet();
        return users.Where(u => !existingUserIds.Contains(u.Id));
    }

    /// <summary>
    ///     選択されたユーザーをメンバーに追加する。
    /// </summary>
    public async Task<Result<bool>> AddMemberAsync(CancellationToken cancellationToken = default)
    {
        if (IsAdding)
        {
            return new Failure(Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage);
        }

        if (Group == null)
        {
            return new Failure("グループが指定されていません。");
        }

        if (SelectedUser == null)
        {
            return new Failure("追加するユーザーを選択してください。");
        }

        if (!IsOwner)
        {
            return new Failure("メンバーを追加する権限がありません。");
        }

        IsAdding = true;
        try
        {
            bool added = await _userGroupDataProvider.AddMemberAsync(
                Group.Id,
                SelectedUser.Id,
                CurrentUserId,
                cancellationToken);

            if (!added)
            {
                return new Failure("メンバーの追加に失敗しました。");
            }

            SelectedUser = null;
            await LoadMembersAsync(cancellationToken);
            return new Success<bool>(true);
        }
        finally
        {
            IsAdding = false;
        }
    }

    /// <summary>
    ///     指定されたユーザーをグループメンバーから削除する。
    /// </summary>
    public async Task<Result<bool>> RemoveMemberAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (Group == null)
        {
            return new Failure("グループが指定されていません。");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return new Failure("削除対象ユーザーが指定されていません。");
        }

        IsRemoving = true;
        try
        {
            bool removed = await _userGroupDataProvider.RemoveMemberAsync(
                Group.Id,
                userId,
                CurrentUserId,
                cancellationToken);

            if (!removed)
            {
                return new Failure("メンバーの削除に失敗しました。");
            }

            await LoadMembersAsync(cancellationToken);
            return new Success<bool>(true);
        }
        finally
        {
            IsRemoving = false;
        }
    }
}