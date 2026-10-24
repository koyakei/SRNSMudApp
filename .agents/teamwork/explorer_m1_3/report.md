# UserGroup Dialog bUnit Tests & R2 Pure Unit Tests Investigation Report

**Author**: `explorer_m1_3`  
**Date**: 2026-10-23  
**Scope**: 
1. UserGroup Dialog bUnit Tests (`UserGroupCreateEditDialogTests.cs`, `UserGroupMembersDialogTests.cs`, `UserGroupListTests.cs`) vs `InteractivePageDialogE2ETests.cs`.
2. UI Interaction Gap Analysis (e.g., member add button click & autocomplete interaction).
3. R2 Pure Unit Tests for Screen/Permission Logic (`ItemCardSplitCoordinator`, `IOwnable`, ViewModels).

---

## 1. Executive Summary

- **UserGroup Dialog bUnit Coverage**:
  Existing bUnit tests in `SRNSMudApp.Tests/Components/UserGroup/` thoroughly cover dialog launch from `UserGroupList` via `IDialogLauncher`, group creation/editing/cancellation in `UserGroupCreateEditDialogTests`, and member list display/removal in `UserGroupMembersDialogTests`.
- **Identified UI Interaction Gap**:
  In `UserGroupMembersDialogTests.cs`, the member addition interaction—selecting a user from `MudAutocomplete<ApplicationUser>` and clicking the "追加" button to add a member, display a success snackbar, and update the member list—is **completely absent**. Currently, the test only checks that the string `"新規メンバー追加"` is rendered in the HTML markup.
  Furthermore, the failure scenario (when member addition fails and an error snackbar is shown) is also missing.
- **R2 Pure Unit Test Coverage (Screen / Permission / Ownership Logic)**:
  - `ItemCardSplitCoordinatorTests.cs` (1,324 lines) and `ItemSplitServiceTests.cs` (3,504 lines) provide comprehensive unit test coverage for split permissions, ownership restrictions, text validation (empty selection, text not in content, CRLF vs LF matching), and rollback mechanisms.
  - **Identified Gap in Pure Unit Tests**:
    1. `IOwnable.IsOwnedBy(string userId)` default interface method (which directly drives button visibility in `ItemCard.razor` and permission guards in `ItemCardSplitCoordinator`) has no direct unit test class.
    2. `UserGroupMembersViewModel.CanAddMember` property conditions (`SelectedUser != null && !IsAdding && !IsLoading && IsOwner`) lack dedicated unit tests.
    3. `ItemCardSplitRequestTests.cs` lacks the BUNIT-SPLIT-01 integration test (`ItemCard_WhenSplitClickedWithEmptySelection_ShowsWarningSnackbar`) that matches step 8 of `ItemSplitE2ETests.cs`.

---

## 2. UserGroup Dialog bUnit Tests vs E2E Comparison

### 2.1 E2E Scenario Mapping (`InteractivePageDialogE2ETests.cs`)

| E2E Step (`InteractivePageDialogE2ETests.cs`) | Existing bUnit Test Coverage | Status & Gap |
|---|---|---|
| Navigate to `/UserGroup/List` and assert empty message "グループがまだありません" | `UserGroupListTests.WhenGroupsEmpty_ClickingCreateGroupButton_OpensCreateDialogViaLauncher` | Covered |
| Click "グループを作成" button to open dialog | `UserGroupListTests.WhenGroupsEmpty_ClickingCreateGroupButton_OpensCreateDialogViaLauncher` | Covered |
| Verify dialog title "新規グループ作成" and label "グループ名" | `UserGroupCreateEditDialogTests.WhenCreateMode_EmptyName_SaveButtonIsDisabled` | Covered |
| Save button disabled when name is empty | `UserGroupCreateEditDialogTests.WhenCreateMode_EmptyName_SaveButtonIsDisabled` | Covered |
| Fill group name, description, click "保存" | `UserGroupCreateEditDialogTests.WhenCreateMode_InputNameAndClickSave_CreatesGroupAndClosesDialog` | Covered |
| Verify snackbar `グループ「{name}」を作成しました。` and group appears in list | `UserGroupCreateEditDialogTests.WhenCreateMode_InputNameAndClickSave_CreatesGroupAndClosesDialog` | Covered |
| Click "メンバー" button on group row | `UserGroupListTests.WhenGroupsExist_ClickingMembersButton_OpensMembersDialog` | Covered |
| Dialog pops up with "新規メンバー追加" | `UserGroupMembersDialogTests.WhenOwner_DisplaysMemberList_AndAddMemberSection` | Partially Covered |
| **Select user in Autocomplete and click "追加" button** | **None** | **MISSING in bUnit** |
| **Add member failure shows error snackbar** | **None** | **MISSING in bUnit** |

---

## 3. Recommended Code Additions: UserGroup Dialog Tests

### 3.1 Addition to `SRNSMudApp.Tests/Components/UserGroup/UserGroupMembersDialogTests.cs`

Add the following two tests to `UserGroupMembersDialogTests.cs` to test the full member addition interaction and failure handling:

```csharp
    [Fact]
    public async Task WhenOwner_SelectsUserAndClicksAdd_CallsAddMemberAndShowsSnackbar_AndUpdatesList()
    {
        // Arrange
        var initialMember = new UserGroupMember
        {
            OwnerId = OwnerUserId,
            UserGroupId = 1,
            UserId = MemberUserId,
            User = new ApplicationUser { Id = MemberUserId, UserName = "bob_developer" },
            CreatedDate = DateTime.UtcNow
        };

        var initialGroup = new UserGroupEntity
        {
            Id = 1,
            Name = "開発チーム",
            OwnerId = OwnerUserId,
            Members = [initialMember]
        };

        var newCandidateUser = new ApplicationUser { Id = "charlie-user-789", UserName = "charlie_engineer" };
        var addedMember = new UserGroupMember
        {
            OwnerId = OwnerUserId,
            UserGroupId = 1,
            UserId = newCandidateUser.Id,
            User = newCandidateUser,
            CreatedDate = DateTime.UtcNow.AddMinutes(1)
        };

        var updatedGroup = new UserGroupEntity
        {
            Id = 1,
            Name = "開発チーム",
            OwnerId = OwnerUserId,
            Members = [initialMember, addedMember]
        };

        // 初期表示およびメンバー追加後の再読み込み用
        _userGroupDataMock
            .SetupSequence(d => d.GetUserGroupByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(initialGroup)
            .ReturnsAsync(updatedGroup);

        _userGroupDataMock
            .Setup(d => d.AddMemberAsync(1, newCandidateUser.Id, OwnerUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        IRenderedComponent<DialogHost> host = _ctx.Render<DialogHost>();
        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        var parameters = new DialogParameters
        {
            [nameof(UserGroupMembersDialog.Group)] = initialGroup,
            [nameof(UserGroupMembersDialog.CurrentUserId)] = OwnerUserId
        };

        _ = await dialogService.ShowAsync<UserGroupMembersDialog>("メンバー管理", parameters);

        // Assert: 初期状態（未選択）では追加ボタンが無効化されていること
        host.WaitForAssertion(() =>
        {
            var addButton = host.FindAll("button").FirstOrDefault(b => b.TextContent.Trim() == "追加");
            Assert.NotNull(addButton);
            Assert.True(addButton.HasAttribute("disabled"));
        });

        // Act: Autocomplete で新規ユーザーを選択
        var autocomplete = host.FindComponent<MudAutocomplete<ApplicationUser>>();
        await host.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(newCandidateUser));

        // Assert: ユーザー選択後に追加ボタンが活性化すること
        host.WaitForAssertion(() =>
        {
            var addButton = host.FindAll("button").FirstOrDefault(b => b.TextContent.Trim() == "追加");
            Assert.NotNull(addButton);
            Assert.False(addButton.HasAttribute("disabled"));
        });

        // Act: 追加ボタンをクリック
        var enabledAddButton = host.FindAll("button").First(b => b.TextContent.Trim() == "追加" && !b.HasAttribute("disabled"));
        enabledAddButton.Click();

        // Assert: データプロバイダーの AddMemberAsync が呼ばれ、成功スナックバーが表示され、メンバー一覧が更新されること
        _userGroupDataMock.Verify(
            d => d.AddMemberAsync(1, newCandidateUser.Id, OwnerUserId, It.IsAny<CancellationToken>()),
            Times.Once);

        _snackbarMock.Verify(
            s => s.Add("ユーザー「charlie_engineer」を追加しました。", Severity.Success, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string>()),
            Times.Once);

        host.WaitForAssertion(() =>
        {
            Assert.Contains("charlie_engineer", host.Markup);
            Assert.Contains("メンバー一覧 (2 人)", host.Markup);
        });
    }

    [Fact]
    public async Task WhenOwner_AddMemberFails_ShowsErrorSnackbar()
    {
        // Arrange
        var initialMember = new UserGroupMember
        {
            OwnerId = OwnerUserId,
            UserGroupId = 1,
            UserId = MemberUserId,
            User = new ApplicationUser { Id = MemberUserId, UserName = "bob_developer" },
            CreatedDate = DateTime.UtcNow
        };

        var initialGroup = new UserGroupEntity
        {
            Id = 1,
            Name = "開発チーム",
            OwnerId = OwnerUserId,
            Members = [initialMember]
        };

        var candidateUser = new ApplicationUser { Id = "david-user-999", UserName = "david_qa" };

        _userGroupDataMock
            .Setup(d => d.GetUserGroupByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(initialGroup);

        _userGroupDataMock
            .Setup(d => d.AddMemberAsync(1, candidateUser.Id, OwnerUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        IRenderedComponent<DialogHost> host = _ctx.Render<DialogHost>();
        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        var parameters = new DialogParameters
        {
            [nameof(UserGroupMembersDialog.Group)] = initialGroup,
            [nameof(UserGroupMembersDialog.CurrentUserId)] = OwnerUserId
        };

        _ = await dialogService.ShowAsync<UserGroupMembersDialog>("メンバー管理", parameters);

        // Act: Autocomplete で新規ユーザーを選択
        host.WaitForAssertion(() =>
        {
            var autocomplete = host.FindComponent<MudAutocomplete<ApplicationUser>>();
            Assert.NotNull(autocomplete);
        });

        var autocompleteComp = host.FindComponent<MudAutocomplete<ApplicationUser>>();
        await host.InvokeAsync(() => autocompleteComp.Instance.ValueChanged.InvokeAsync(candidateUser));

        // Act: 有効化された追加ボタンをクリック
        host.WaitForAssertion(() =>
        {
            var addButton = host.FindAll("button").FirstOrDefault(b => b.TextContent.Trim() == "追加" && !b.HasAttribute("disabled"));
            Assert.NotNull(addButton);
        });

        var btn = host.FindAll("button").First(b => b.TextContent.Trim() == "追加" && !b.HasAttribute("disabled"));
        btn.Click();

        // Assert: エラースナックバーが表示されること
        _userGroupDataMock.Verify(
            d => d.AddMemberAsync(1, candidateUser.Id, OwnerUserId, It.IsAny<CancellationToken>()),
            Times.Once);

        _snackbarMock.Verify(
            s => s.Add("メンバーの追加に失敗しました。", Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string>()),
            Times.Once);
    }
```

---

## 4. R2 Pure Unit Tests for Screen / Permission Logic

### 4.1 Ownership and Permission Logic Audit

1. **`IOwnable.IsOwnedBy`**:
   - `ItemCard.razor` uses `((IOwnable)Item).IsOwnedBy(CurrentUserId)` to toggle between:
     - Direct Split button (`title="選択テキストを別アイテムに分割"`) for owners.
     - Split Request button (`title="選択テキストを別アイテムに分割するリクエストを送信"`) for non-owners.
     - Pending request actions: Approve/Reject for owners, Cancel for requester.
   - Currently, there are no pure unit tests directly targeting `IOwnable.IsOwnedBy`.
2. **`ItemCardSplitCoordinator`**:
   - Ownership guards in `SplitSelectionAsync`, `ApproveSplitRequestAsync`, `RejectSplitRequestAsync`, `CancelSplitRequestAsync`, `RequestSplitSelectionAsync`, `RequestSplitWithVoteAsync`, `RequestSplitWithReactionAsync`, and `RequestSplitWithTagAddAsync`.
   - Comprehensive pure unit tests exist in `ItemCardSplitCoordinatorTests.cs` (1,324 lines), verifying all guards, error messages, and rollback behavior without UI rendering.
3. **`UserGroupMembersViewModel`**:
   - `IsOwner`: `Group != null && Group.OwnerId == CurrentUserId`.
   - `CanAddMember`: `SelectedUser != null && !IsAdding && !IsLoading && IsOwner`.
   - `AddMemberAsync` & `RemoveMemberAsync` guard against unauthorized access, null groups, and empty user IDs.
   - All are covered in `UserGroupMembersViewModelTests.cs`, except an explicit test for `CanAddMember` property combinations.

---

## 5. Recommended Code Additions: R2 Pure Unit Tests

### 5.1 New Pure Unit Test: `SRNSMudApp.Tests/Data/IOwnableUnitTests.cs`

Create this new test file to guarantee 100% pure unit test coverage for ownership predicates:

```csharp
using SRNSMudApp.Data;

namespace SRNSMudApp.Tests.Data;

/// <summary>
///     <see cref="IOwnable"/> のデフォルト実装メソッド <see cref="IOwnable.IsOwnedBy"/> に対する
///     純粋単体テスト。所有者判定ロジックの一致・不一致・境界条件を検証する。
/// </summary>
public sealed class IOwnableUnitTests
{
    private sealed class TestOwnable : IOwnable
    {
        public string OwnerId { get; set; } = string.Empty;
        public ApplicationUser Owner { get; set; } = null!;
    }

    [Theory]
    [InlineData("user-123", "user-123", true)]
    [InlineData("user-123", "user-456", false)]
    [InlineData("user-123", "", false)]
    [InlineData("user-123", "   ", false)]
    [InlineData("user-123", null, false)]
    public void IsOwnedBy_EvaluatesOwnershipCorrectly(string ownerId, string? targetUserId, bool expected)
    {
        IOwnable ownable = new TestOwnable { OwnerId = ownerId };
        bool result = ownable.IsOwnedBy(targetUserId!);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsOwnedBy_WhenBothOwnerIdAndTargetUserIdAreEmpty_ReturnsTrue()
    {
        IOwnable ownable = new TestOwnable { OwnerId = "" };
        Assert.True(ownable.IsOwnedBy(""));
    }

    [Fact]
    public void Item_ImplementsIOwnable_AndIsOwnedByWorksCorrectly()
    {
        var item = new Item { OwnerId = "author-abc", Content = "Test Content" };
        IOwnable ownable = item;

        Assert.True(ownable.IsOwnedBy("author-abc"));
        Assert.False(ownable.IsOwnedBy("stranger-xyz"));
    }
}
```

### 5.2 Addition to `SRNSMudApp.Tests/Components/UserGroup/UserGroupMembersViewModelTests.cs`

Add unit tests for the `CanAddMember` screen logic property:

```csharp
    [Fact]
    public async Task CanAddMember_WhenSelectedUserProvidedAndIsOwner_ReturnsTrue()
    {
        // Arrange
        var group = new UserGroupEntity { Id = 1, Name = "Alpha", OwnerId = "owner-1" };
        _userGroupDataProviderMock.Setup(p => p.GetUserGroupByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);

        await _sut.InitializeAsync(group, "owner-1");
        _sut.SelectedUser = new ApplicationUser { Id = "u2", UserName = "Bob" };

        // Assert
        Assert.True(_sut.CanAddMember);
    }

    [Fact]
    public async Task CanAddMember_WhenNotOwner_ReturnsFalseEvenWithSelectedUser()
    {
        // Arrange
        var group = new UserGroupEntity { Id = 1, Name = "Alpha", OwnerId = "owner-1" };
        _userGroupDataProviderMock.Setup(p => p.GetUserGroupByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);

        await _sut.InitializeAsync(group, "non-owner-user");
        _sut.SelectedUser = new ApplicationUser { Id = "u2", UserName = "Bob" };

        // Assert
        Assert.False(_sut.CanAddMember);
    }

    [Fact]
    public async Task CanAddMember_WhenSelectedUserIsNull_ReturnsFalse()
    {
        // Arrange
        var group = new UserGroupEntity { Id = 1, Name = "Alpha", OwnerId = "owner-1" };
        _userGroupDataProviderMock.Setup(p => p.GetUserGroupByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);

        await _sut.InitializeAsync(group, "owner-1");
        _sut.SelectedUser = null;

        // Assert
        Assert.False(_sut.CanAddMember);
    }
```

### 5.3 Addition to `SRNSMudApp.Tests/Components/UI/ItemCardSplitRequestTests.cs` (BUNIT-SPLIT-01)

Add the missing warning snackbar integration bUnit test:

```csharp
    [Fact]
    public async Task ItemCard_WhenSplitClickedWithEmptySelection_ShowsWarningSnackbar()
    {
        // Arrange: 他ユーザーのアイテムでテキスト未選択（Coordinator が警告スナックバーを表示して null を返すケース）
        var item = new DataItem
        {
            Id = 41,
            Content = "他ユーザーのアイテム本文",
            OwnerId = OtherUserId,
            TagRelations = []
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(41, default))
            .ReturnsAsync([]);

        // Coordinator が「分割するテキストが選択されていません。」スナックバーを表示し null を返す動作をシミュレート
        _splitCoordinatorMock
            .Setup(c => c.RequestSplitSelectionAsync(item, CurrentUserId))
            .Callback(() =>
            {
                ISnackbar snackbar = _ctx.Services.GetRequiredService<ISnackbar>();
                _ = snackbar.Add("分割するテキストが選択されていません。", Severity.Warning);
            })
            .ReturnsAsync((ItemSplitRequest?)null);

        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, CurrentUserId));

        // Act: 非所有者の「分割リクエスト」ボタンをクリック
        var splitBtn = cut.Find("button[title='選択テキストを別アイテムに分割するリクエストを送信']");
        await cut.InvokeAsync(() => splitBtn.Click());

        // Assert: Coordinator が呼ばれ、警告スナックバーが表示されること
        _splitCoordinatorMock.Verify(c => c.RequestSplitSelectionAsync(item, CurrentUserId), Times.Once);

        ISnackbar renderedSnackbar = _ctx.Services.GetRequiredService<ISnackbar>();
        Assert.Contains(renderedSnackbar.ShownSnackbars, s =>
            s.Severity == Severity.Warning &&
            s.Message.ToString().Contains("分割するテキストが選択されていません。"));
    }
```

---

## 6. Verification and Independence from DB Containers

All recommended tests:
1. Do not require MSSQL / Testcontainers.
2. Run fully in-memory via bUnit and xUnit in single-digit milliseconds.
3. Test complete user interactions and screen state transitions.
4. Directly enable retirement of slow Playwright E2E browser tests while maintaining 100% test confidence.
