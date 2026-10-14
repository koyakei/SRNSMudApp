namespace SRNSMudApp.Client.Components;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;

using MudBlazor;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Client.Services;

/// <summary>
/// ユーザーグループ管理 WASM コンポーネント。
/// </summary>
public partial class UserGroupView : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    [Inject]
    public required IItemListApiClient ApiClient { get; set; }

    [Inject]
    public required ISnackbar Snackbar { get; set; }

    public IReadOnlyList<UserGroupDto> Groups { get; private set; } = [];

    public bool IsLoading { get; private set; } = true;

    public bool ShowCreateForm { get; private set; }

    public bool IsCreating { get; private set; }

    public string NewGroupName { get; set; } = string.Empty;

    public string? NewGroupDescription { get; set; }

    public int? EditingGroupId { get; private set; }

    public string EditGroupName { get; set; } = string.Empty;

    public string? EditGroupDescription { get; set; }

    public int? SelectedGroupId { get; private set; }

    public UserGroupDetailDto? SelectedGroupDetail { get; private set; }

    public bool IsLoadingDetail { get; private set; }

    public string NewMemberUserId { get; set; } = string.Empty;

    public bool IsAddingMember { get; private set; }

    protected override async Task OnInitializedAsync()
    {
        await LoadGroupsAsync();
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    public async Task LoadGroupsAsync()
    {
        IsLoading = true;

        try
        {
            Groups = await ApiClient.GetUserGroupsAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            // キャンセル時は無視
        }
        catch (Exception ex)
        {
            Snackbar.Add($"グループ一覧の取得に失敗しました: {ex.Message}", Severity.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void ToggleCreateForm()
    {
        ShowCreateForm = !ShowCreateForm;
        if (!ShowCreateForm)
        {
            NewGroupName = string.Empty;
            NewGroupDescription = null;
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    public async Task CreateGroupAsync()
    {
        if (string.IsNullOrWhiteSpace(NewGroupName))
        {
            return;
        }

        IsCreating = true;

        try
        {
            var request = new CreateUserGroupRequest(NewGroupName.Trim(), NewGroupDescription?.Trim());
            UserGroupDto? created = await ApiClient.CreateUserGroupAsync(request, _cts.Token);

            if (created != null)
            {
                Snackbar.Add($"グループ「{created.Name}」を作成しました。", Severity.Success);
                NewGroupName = string.Empty;
                NewGroupDescription = null;
                ShowCreateForm = false;
                await LoadGroupsAsync();
            }
            else
            {
                Snackbar.Add("グループの作成に失敗しました。ログイン状態を確認してください。", Severity.Error);
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"グループ作成エラー: {ex.Message}", Severity.Error);
        }
        finally
        {
            IsCreating = false;
        }
    }

    public void StartEditGroup(UserGroupDto group)
    {
        ArgumentNullException.ThrowIfNull(group);
        EditingGroupId = group.Id;
        EditGroupName = group.Name;
        EditGroupDescription = group.Description;
    }

    public void CancelEdit()
    {
        EditingGroupId = null;
        EditGroupName = string.Empty;
        EditGroupDescription = null;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    public async Task SaveEditGroupAsync(int groupId)
    {
        if (string.IsNullOrWhiteSpace(EditGroupName))
        {
            Snackbar.Add("グループ名は必須です。", Severity.Warning);
            return;
        }

        try
        {
            var request = new UpdateUserGroupRequest(EditGroupName.Trim(), EditGroupDescription?.Trim());
            bool success = await ApiClient.UpdateUserGroupAsync(groupId, request, _cts.Token);

            if (success)
            {
                Snackbar.Add("グループ情報を更新しました。", Severity.Success);
                CancelEdit();
                await LoadGroupsAsync();
            }
            else
            {
                Snackbar.Add("グループ更新に失敗しました。権限がありません。", Severity.Error);
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"グループ更新エラー: {ex.Message}", Severity.Error);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    public async Task DeleteGroupAsync(int groupId)
    {
        try
        {
            bool success = await ApiClient.DeleteUserGroupAsync(groupId, _cts.Token);
            if (success)
            {
                Snackbar.Add("グループを削除しました。", Severity.Info);
                if (SelectedGroupId == groupId)
                {
                    SelectedGroupId = null;
                    SelectedGroupDetail = null;
                }

                await LoadGroupsAsync();
            }
            else
            {
                Snackbar.Add("グループの削除に失敗しました。オーナー権限が必要です。", Severity.Error);
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"グループ削除エラー: {ex.Message}", Severity.Error);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    public async Task ToggleDetailAsync(int groupId)
    {
        if (SelectedGroupId == groupId)
        {
            CloseDetail();
            return;
        }

        SelectedGroupId = groupId;
        SelectedGroupDetail = null;
        IsLoadingDetail = true;

        try
        {
            SelectedGroupDetail = await ApiClient.GetUserGroupDetailAsync(groupId, _cts.Token);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"メンバー情報の取得に失敗しました: {ex.Message}", Severity.Error);
        }
        finally
        {
            IsLoadingDetail = false;
        }
    }

    public void CloseDetail()
    {
        SelectedGroupId = null;
        SelectedGroupDetail = null;
        NewMemberUserId = string.Empty;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    public async Task AddMemberAsync(int groupId)
    {
        if (string.IsNullOrWhiteSpace(NewMemberUserId))
        {
            return;
        }

        IsAddingMember = true;

        try
        {
            var request = new AddGroupMemberRequest(NewMemberUserId.Trim());
            bool success = await ApiClient.AddUserGroupMemberAsync(groupId, request, _cts.Token);

            if (success)
            {
                Snackbar.Add("メンバーを追加しました。", Severity.Success);
                NewMemberUserId = string.Empty;
                // 詳細を再取得
                SelectedGroupDetail = await ApiClient.GetUserGroupDetailAsync(groupId, _cts.Token);
            }
            else
            {
                Snackbar.Add("メンバー追加に失敗しました。ユーザーIDと権限を確認してください。", Severity.Error);
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"メンバー追加エラー: {ex.Message}", Severity.Error);
        }
        finally
        {
            IsAddingMember = false;
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    public async Task RemoveMemberAsync(int groupId, string userId)
    {
        try
        {
            bool success = await ApiClient.RemoveUserGroupMemberAsync(groupId, userId, _cts.Token);
            if (success)
            {
                Snackbar.Add("メンバーを削除しました。", Severity.Info);
                SelectedGroupDetail = await ApiClient.GetUserGroupDetailAsync(groupId, _cts.Token);
            }
            else
            {
                Snackbar.Add("メンバー削除に失敗しました。", Severity.Error);
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"メンバー削除エラー: {ex.Message}", Severity.Error);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}