using Microsoft.AspNetCore.Identity;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

namespace SRNSMudApp.Components.User;

/// <summary>
///     UserDetail コンポーネントの状態管理、データロード、ユーザー操作（フォロー、管理者昇格など）を集約する ViewModel。
///     UI（Blazor / bUnit）から切り離し、純粋な xUnit + Moq 単体テストを可能にする。
/// </summary>
public class UserDetailViewModel
{
    private readonly IUserDataProvider _userDataProvider;
    private readonly UserManager<ApplicationUser>? _userManager;
    private readonly IServiceScopeFactory? _scopeFactory;

    public UserDetailViewModel(
        IUserDataProvider userDataProvider,
        UserManager<ApplicationUser>? userManager = null,
        IServiceScopeFactory? scopeFactory = null)
    {
        _userDataProvider = userDataProvider ?? throw new ArgumentNullException(nameof(userDataProvider));
        _userManager = userManager;
        _scopeFactory = scopeFactory;
    }

    public string UserId { get; private set; } = string.Empty;
    public string? CurrentUserId { get; private set; }
    public bool IsLoggedIn { get; private set; }
    public bool IsLoading { get; private set; } = true;

    public ApplicationUser? User { get; private set; }
    public UserProfileDto? UserProfile { get; private set; }
    public IReadOnlyList<Data.Tag> UserTags { get; private set; } = [];
    public IReadOnlyList<Data.Item> UserItems { get; private set; } = [];
    public IReadOnlyList<Data.Tag> ReactionTags { get; private set; } = [];
    public bool IsFollowing { get; private set; }
    public int FollowingCount { get; private set; }
    public int FollowersCount { get; private set; }
    public IReadOnlyList<ApplicationUser> FollowingUsers { get; private set; } = [];
    public IReadOnlyList<ApplicationUser> FollowerUsers { get; private set; } = [];

    public bool CanFollow => IsLoggedIn && !string.IsNullOrEmpty(CurrentUserId) && CurrentUserId != UserId && User != null;

    /// <summary>
    ///     ユーザー詳細画面の状態を初期化し、データをロードする。
    /// </summary>
    public async Task InitializeAsync(string userId, string? currentUserId, bool isLoggedIn)
    {
        UserId = userId ?? string.Empty;
        CurrentUserId = currentUserId;
        IsLoggedIn = isLoggedIn;

        await LoadDataAsync();
    }

    /// <summary>
    ///     ユーザー詳細データを再読み込みする。
    /// </summary>
    public async Task LoadDataAsync()
    {
        if (string.IsNullOrEmpty(UserId))
        {
            User = null;
            IsLoading = false;
            return;
        }

        try
        {
            IsLoading = true;
            Task<UserDetailPageData>? detailTask = _userDataProvider.GetUserDetailAsync(UserId, CurrentUserId);
            UserDetailPageData? page = detailTask is not null ? await detailTask : null;
            if (page is null)
            {
                Task<UserDetailPageData>? fallbackTask = _userDataProvider.GetUserDetailAsync(UserId);
                page = fallbackTask is not null ? await fallbackTask : null;
            }

            User = page?.User;
            UserProfile = page?.UserProfile;

            if (page is not null)
            {
                UserTags = page.UserTags;
                ReactionTags = page.ReactionTags ?? UserTags
                    .Where(t => ReactionTagNames.IsReactionTagName(t.Name))
                    .OrderBy(t => GetReactionOrder(t.Name))
                    .ToList();
                UserItems = page.UserItems;
                IsFollowing = page.IsFollowing;
                FollowingCount = page.FollowingCount;
                FollowersCount = page.FollowersCount;
                FollowingUsers = page.FollowingUsers ?? [];
                FollowerUsers = page.FollowerUsers ?? [];
            }
            else
            {
                UserTags = [];
                ReactionTags = [];
                UserItems = [];
                IsFollowing = false;
                FollowingCount = 0;
                FollowersCount = 0;
                FollowingUsers = [];
                FollowerUsers = [];
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    ///     現在のユーザーに対するフォロー状態をトグルする。
    /// </summary>
    public async Task<bool> ToggleFollowAsync()
    {
        if (!CanFollow)
        {
            return false;
        }

        var newFollowingStatus = await _userDataProvider.ToggleFollowUserAsync(CurrentUserId!, UserId);
        IsFollowing = newFollowingStatus;
        if (IsFollowing)
        {
            FollowersCount++;
        }
        else
        {
            FollowersCount = Math.Max(0, FollowersCount - 1);
        }

        await LoadDataAsync();
        return newFollowingStatus;
    }

    /// <summary>
    ///     互換用: 指定されたユーザーID間でフォロー状態をトグルする。
    /// </summary>
    public async Task<bool> ToggleFollowAsync(string currentUserId, string targetUserId)
    {
        if (string.IsNullOrEmpty(currentUserId) || string.IsNullOrEmpty(targetUserId) || currentUserId == targetUserId)
        {
            return false;
        }

        return await _userDataProvider.ToggleFollowUserAsync(currentUserId, targetUserId);
    }

    /// <summary>
    ///     ユーザーを Admin ロールへ昇格する。
    /// </summary>
    public async Task<IdentityResult> MakeAdminAsync(string targetUserId)
    {
        if (string.IsNullOrEmpty(targetUserId))
        {
            return IdentityResult.Failed(new IdentityError { Description = "ユーザーIDが指定されていません。" });
        }

        // DATA-03: Blazor Circuit 内で長期生存する UserManager (および付随する Scoped DbContext) との並行競合を防ぐため、
        // 短命なスコープ内で UserManager を解決して操作する
        if (_scopeFactory != null)
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            var scopedUserManager = scope.ServiceProvider.GetService<UserManager<ApplicationUser>>();
            if (scopedUserManager == null)
            {
                return IdentityResult.Failed(new IdentityError { Description = "UserManager が利用できません。" });
            }

            ApplicationUser? scopedUser = await scopedUserManager.FindByIdAsync(targetUserId);
            if (scopedUser == null)
            {
                return IdentityResult.Failed(new IdentityError { Description = "ユーザーが見つかりません。" });
            }

            return await scopedUserManager.AddToRoleAsync(scopedUser, "Admin");
        }

        if (_userManager == null)
        {
            return IdentityResult.Failed(new IdentityError { Description = "UserManager が利用できません。" });
        }

        ApplicationUser? user = await _userManager.FindByIdAsync(targetUserId);
        if (user == null)
        {
            return IdentityResult.Failed(new IdentityError { Description = "ユーザーが見つかりません。" });
        }

        return await _userManager.AddToRoleAsync(user, "Admin");
    }

    private static int GetReactionOrder(string name) => name switch
    {
        ReactionTagNames.Shinji => 0,
        ReactionTagNames.Zen => 1,
        ReactionTagNames.Bi => 2,
        _ => int.MaxValue
    };
}