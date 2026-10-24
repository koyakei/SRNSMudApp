using System.Security.Claims;

using Bunit;
using Bunit.TestDoubles;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.UserGroup;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

using UserGroupEntity = SRNSMudApp.Data.UserGroup;

namespace SRNSMudApp.Tests.Components.UserGroup;

/// <summary>
///     <see cref="UserGroupMembersDialog"/> の UI インタラクションテスト。
///     オーナー/一般メンバー表示の権限出し分け、メンバー削除ボタンクリックによるサービス連携と
///     スナックバー通知、およびダイアログ終了動作を検証する。
/// </summary>
public class UserGroupMembersDialogTests : IAsyncDisposable
{
    private const string OwnerUserId = "owner-user-123";
    private const string MemberUserId = "member-user-456";

    private readonly BunitContext _ctx = new();
    private readonly Mock<IUserGroupDataProvider> _userGroupDataMock = new();
    private readonly Mock<IUserDataProvider> _userDataMock = new();
    private readonly Mock<ISnackbar> _snackbarMock = new();

    public UserGroupMembersDialogTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();

        _ctx.Services.RemoveAll<IUserGroupDataProvider>();
        _ctx.Services.AddScoped(_ => _userGroupDataMock.Object);

        _ctx.Services.RemoveAll<IUserDataProvider>();
        _ctx.Services.AddScoped(_ => _userDataMock.Object);

        _ctx.Services.RemoveAll<ISnackbar>();
        _ctx.Services.AddScoped(_ => _snackbarMock.Object);

        _ctx.Services.AddTransient<UserGroupMembersViewModel>();

        var authContext = _ctx.AddAuthorization();
        authContext.SetAuthorized("OwnerUser");
        authContext.SetClaims(
            new Claim(ClaimTypes.NameIdentifier, OwnerUserId),
            new Claim(ClaimTypes.Name, "OwnerUser"));

        _ = _ctx.Render<MudPopoverProvider>();
    }

    [Fact]
    public async Task WhenOwner_DisplaysMemberList_AndAddMemberSection()
    {
        // Arrange
        var memberUser = new ApplicationUser { Id = MemberUserId, UserName = "bob_developer" };
        var member = new UserGroupMember
        {
            OwnerId = OwnerUserId,
            UserGroupId = 1,
            UserId = MemberUserId,
            User = memberUser,
            CreatedDate = DateTime.UtcNow
        };

        var group = new UserGroupEntity
        {
            Id = 1,
            Name = "開発チーム",
            OwnerId = OwnerUserId,
            Members = [member]
        };

        _userGroupDataMock
            .Setup(d => d.GetUserGroupByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);

        IRenderedComponent<DialogHost> host = _ctx.Render<DialogHost>();
        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        var parameters = new DialogParameters
        {
            [nameof(UserGroupMembersDialog.Group)] = group,
            [nameof(UserGroupMembersDialog.CurrentUserId)] = OwnerUserId
        };

        // Act
        _ = await dialogService.ShowAsync<UserGroupMembersDialog>("メンバー管理", parameters);

        // Assert: オーナー向け追加セクションとメンバーが表示されていることを検証
        host.WaitForAssertion(() =>
        {
            Assert.Contains("新規メンバー追加", host.Markup);
            Assert.Contains("bob_developer", host.Markup);
            Assert.Contains("メンバー一覧 (1 人)", host.Markup);
        });
    }

    [Fact]
    public async Task WhenNotOwner_DoesNotDisplayAddMemberSection_NorRemoveButton()
    {
        // Arrange: 一般メンバーとしてダイアログを開く
        var member = new UserGroupMember
        {
            OwnerId = OwnerUserId,
            UserGroupId = 1,
            UserId = MemberUserId,
            User = new ApplicationUser { Id = MemberUserId, UserName = "bob_developer" },
            CreatedDate = DateTime.UtcNow
        };

        var group = new UserGroupEntity
        {
            Id = 1,
            Name = "開発チーム",
            OwnerId = OwnerUserId,
            Members = [member]
        };

        _userGroupDataMock
            .Setup(d => d.GetUserGroupByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);

        IRenderedComponent<DialogHost> host = _ctx.Render<DialogHost>();
        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        var parameters = new DialogParameters
        {
            [nameof(UserGroupMembersDialog.Group)] = group,
            [nameof(UserGroupMembersDialog.CurrentUserId)] = MemberUserId // 非オーナー
        };

        // Act
        _ = await dialogService.ShowAsync<UserGroupMembersDialog>("メンバー管理", parameters);

        // Assert: 追加セクションおよび削除ボタンが表示されないことを検証
        host.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("新規メンバー追加", host.Markup);
            Assert.Contains("bob_developer", host.Markup);
            var removeButtons = host.FindAll("button[title='メンバーから削除']");
            Assert.Empty(removeButtons);
        });
    }

    [Fact]
    public async Task WhenOwner_ClickingRemoveMemberButton_CallsRemoveAndShowsSnackbar()
    {
        // Arrange
        var member = new UserGroupMember
        {
            OwnerId = OwnerUserId,
            UserGroupId = 1,
            UserId = MemberUserId,
            User = new ApplicationUser { Id = MemberUserId, UserName = "bob_developer" },
            CreatedDate = DateTime.UtcNow
        };

        var group = new UserGroupEntity
        {
            Id = 1,
            Name = "開発チーム",
            OwnerId = OwnerUserId,
            Members = [member]
        };

        _userGroupDataMock
            .Setup(d => d.GetUserGroupByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);

        _userGroupDataMock
            .Setup(d => d.RemoveMemberAsync(1, MemberUserId, OwnerUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        IRenderedComponent<DialogHost> host = _ctx.Render<DialogHost>();
        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        var parameters = new DialogParameters
        {
            [nameof(UserGroupMembersDialog.Group)] = group,
            [nameof(UserGroupMembersDialog.CurrentUserId)] = OwnerUserId
        };

        _ = await dialogService.ShowAsync<UserGroupMembersDialog>("メンバー管理", parameters);

        // Act: 削除ボタンをクリック
        host.WaitForAssertion(() =>
        {
            var removeButton = host.FindAll("button").FirstOrDefault(b => b.GetAttribute("title") == "メンバーから削除");
            Assert.NotNull(removeButton);
        });

        var removeBtn = host.FindAll("button").First(b => b.GetAttribute("title") == "メンバーから削除");
        removeBtn.Click();

        // Assert: メンバー削除処理が呼ばれ、スナックバーが表示されたことを検証
        _userGroupDataMock.Verify(
            d => d.RemoveMemberAsync(1, MemberUserId, OwnerUserId, It.IsAny<CancellationToken>()),
            Times.Once);

        _snackbarMock.Verify(
            s => s.Add("メンバーを削除しました。", Severity.Success, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task ClickingCloseButton_ClosesDialog()
    {
        // Arrange
        var group = new UserGroupEntity
        {
            Id = 1,
            Name = "開発チーム",
            OwnerId = OwnerUserId,
            Members = []
        };

        _userGroupDataMock
            .Setup(d => d.GetUserGroupByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);

        IRenderedComponent<DialogHost> host = _ctx.Render<DialogHost>();
        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        var parameters = new DialogParameters
        {
            [nameof(UserGroupMembersDialog.Group)] = group,
            [nameof(UserGroupMembersDialog.CurrentUserId)] = OwnerUserId
        };

        var dialogRef = await dialogService.ShowAsync<UserGroupMembersDialog>("メンバー管理", parameters);

        // Act: 閉じるボタンをクリック
        var closeButton = host.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("閉じる"));
        Assert.NotNull(closeButton);
        closeButton.Click();

        // Assert: ダイアログが閉じたことを検証
        var result = await dialogRef.Result;
        Assert.NotNull(result);
        Assert.False(result.Canceled);
    }

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

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private sealed class DialogHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MudDialogProvider>(0);
            builder.CloseComponent();
        }
    }
}