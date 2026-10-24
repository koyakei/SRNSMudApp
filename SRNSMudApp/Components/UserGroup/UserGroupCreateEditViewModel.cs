#region

using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

using UserGroupEntity = SRNSMudApp.Data.UserGroup;

#endregion

namespace SRNSMudApp.Components.UserGroup;

/// <summary>
///     ユーザーグループ作成・編集ダイアログ用 ViewModel。
///     グループ名・説明の入力状態管理、バリデーション、および作成・更新処理をカプセル化する。
/// </summary>
public sealed class UserGroupCreateEditViewModel
{
    private readonly IUserGroupDataProvider _userGroupDataProvider;

    public UserGroupCreateEditViewModel(IUserGroupDataProvider userGroupDataProvider)
    {
        _userGroupDataProvider = userGroupDataProvider ?? throw new ArgumentNullException(nameof(userGroupDataProvider));
    }

    public UserGroupEntity? Group { get; private set; }
    public string CurrentUserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSaving { get; private set; }

    public bool IsEditMode => Group != null;

    public bool CanSave =>
        !IsSaving &&
        !string.IsNullOrWhiteSpace(Name) &&
        !string.IsNullOrWhiteSpace(CurrentUserId);

    /// <summary>
    ///     対象グループ（新規作成の場合は null）および現在のユーザーIDを設定して初期化する。
    /// </summary>
    public void Initialize(UserGroupEntity? group, string currentUserId)
    {
        Group = group;
        CurrentUserId = currentUserId ?? string.Empty;
        Name = group?.Name ?? string.Empty;
        Description = group?.Description ?? string.Empty;
        IsSaving = false;
    }

    /// <summary>
    ///     グループの作成または更新を保存する。
    /// </summary>
    public async Task<Result<UserGroupEntity>> SaveAsync(CancellationToken cancellationToken = default)
    {
        if (IsSaving)
        {
            return new Failure(Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage);
        }

        if (string.IsNullOrWhiteSpace(CurrentUserId))
        {
            return new Failure("ユーザー情報が取得できませんでした。");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            return new Failure("グループ名は必須です。");
        }

        IsSaving = true;
        try
        {
            string trimmedName = Name.Trim();
            string trimmedDesc = Description.Trim();

            if (Group == null)
            {
                UserGroupEntity created = await _userGroupDataProvider.CreateUserGroupAsync(
                    trimmedName,
                    trimmedDesc,
                    CurrentUserId,
                    cancellationToken);

                return new Success<UserGroupEntity>(created);
            }

            bool updated = await _userGroupDataProvider.UpdateUserGroupAsync(
                Group.Id,
                trimmedName,
                trimmedDesc,
                CurrentUserId,
                cancellationToken);

            if (!updated)
            {
                return new Failure("グループの更新権限がないか、見つかりませんでした。");
            }

            Group.Name = trimmedName;
            Group.Description = trimmedDesc;
            return new Success<UserGroupEntity>(Group);
        }
        finally
        {
            IsSaving = false;
        }
    }
}