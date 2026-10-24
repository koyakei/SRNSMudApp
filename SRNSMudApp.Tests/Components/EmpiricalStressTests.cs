using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Bunit;
using Bunit.TestDoubles;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Contract;
using SRNSMudApp.Components.UserGroup;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

using Xunit;

using UserGroupEntity = SRNSMudApp.Data.UserGroup;

namespace SRNSMudApp.Tests.Components;

/// <summary>
///     Empirical stress and mutation verification tests created by challenger_m1_1.
///     Verifies that tests fail under mutated conditions and that DOM assertions are strict.
/// </summary>
public class EmpiricalStressTests
{
    private static BunitContext CreateContext()
    {
        var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddMudServices();
        return ctx;
    }

    [Fact]
    public async Task ContractManagement_WithoutTabClick_OutboxAlertIsNeverRendered()
    {
        var ctx = CreateContext();
        try
        {
            var contractDataMock = new Mock<IContractManagementDataProvider>();
            contractDataMock
                .Setup(d => d.GetContractsAsync("u1"))
                .ReturnsAsync(new ContractManagementPageData([], []));

            ctx.Services.RemoveAll<IContractManagementDataProvider>();
            ctx.Services.AddScoped(_ => contractDataMock.Object);
            ctx.Services.RemoveAll<ITaggingContractService>();
            ctx.Services.AddScoped(_ => new Mock<ITaggingContractService>().Object);
            ctx.Services.RemoveAll<ISnackbar>();
            ctx.Services.AddScoped(_ => new Mock<ISnackbar>().Object);
            ctx.Services.AddTransient<ContractManagementViewModel>();

            var authContext = ctx.AddAuthorization();
            authContext.SetAuthorized("User1");
            authContext.SetClaims(new Claim(ClaimTypes.NameIdentifier, "u1"));

            var cut = ctx.Render<ContractManagement>();

            // Assert: Without clicking outbox tab, Outbox alert NEVER appears
            Assert.Contains("現在、受信したコントラクトの提案はありません。", cut.Markup);
            Assert.DoesNotContain("現在、送信したコントラクトの提案はありません。", cut.Markup);

            // Verify that asserting Outbox message without clicking tab throws exception (test would fail!)
            Assert.ThrowsAny<Exception>(() =>
            {
                cut.WaitForAssertion(() =>
                    Assert.Contains("現在、送信したコントラクトの提案はありません。", cut.Markup),
                    timeout: TimeSpan.FromMilliseconds(50));
            });
        }
        finally
        {
            await ctx.DisposeAsync();
        }
    }

    [Fact]
    public async Task ContractManagement_WhenSwitchedToOutbox_InboxAlertIsRemoved()
    {
        var ctx = CreateContext();
        try
        {
            var contractDataMock = new Mock<IContractManagementDataProvider>();
            contractDataMock
                .Setup(d => d.GetContractsAsync("u1"))
                .ReturnsAsync(new ContractManagementPageData([], []));

            ctx.Services.RemoveAll<IContractManagementDataProvider>();
            ctx.Services.AddScoped(_ => contractDataMock.Object);
            ctx.Services.RemoveAll<ITaggingContractService>();
            ctx.Services.AddScoped(_ => new Mock<ITaggingContractService>().Object);
            ctx.Services.RemoveAll<ISnackbar>();
            ctx.Services.AddScoped(_ => new Mock<ISnackbar>().Object);
            ctx.Services.AddTransient<ContractManagementViewModel>();

            var authContext = ctx.AddAuthorization();
            authContext.SetAuthorized("User1");
            authContext.SetClaims(new Claim(ClaimTypes.NameIdentifier, "u1"));

            var cut = ctx.Render<ContractManagement>();

            var outboxTab = cut.FindAll(".mud-tab").First(t => t.TextContent.Contains("送信済み (Outbox)"));
            outboxTab.Click();

            cut.WaitForAssertion(() =>
            {
                Assert.Contains("現在、送信したコントラクトの提案はありません。", cut.Markup);
                // Strict assertion: Inbox alert is unmounted and no longer in markup
                Assert.DoesNotContain("現在、受信したコントラクトの提案はありません。", cut.Markup);
            });
        }
        finally
        {
            await ctx.DisposeAsync();
        }
    }

    [Fact]
    public async Task UserGroupMembersDialog_WhenAddFails_ListRemainsUnchangedAndErrorSnackbarShown()
    {
        var ctx = CreateContext();
        try
        {
            var userGroupDataMock = new Mock<IUserGroupDataProvider>();
            var userDataMock = new Mock<IUserDataProvider>();
            var snackbarMock = new Mock<ISnackbar>();

            ctx.Services.RemoveAll<IUserGroupDataProvider>();
            ctx.Services.AddScoped(_ => userGroupDataMock.Object);
            ctx.Services.RemoveAll<IUserDataProvider>();
            ctx.Services.AddScoped(_ => userDataMock.Object);
            ctx.Services.RemoveAll<ISnackbar>();
            ctx.Services.AddScoped(_ => snackbarMock.Object);
            ctx.Services.AddTransient<UserGroupMembersViewModel>();

            var authContext = ctx.AddAuthorization();
            authContext.SetAuthorized("Owner");
            authContext.SetClaims(new Claim(ClaimTypes.NameIdentifier, "owner-1"));

            _ = ctx.Render<MudPopoverProvider>();

            var initialMember = new UserGroupMember
            {
                OwnerId = "owner-1",
                UserGroupId = 1,
                UserId = "m1",
                User = new ApplicationUser { Id = "m1", UserName = "member1" },
                CreatedDate = DateTime.UtcNow
            };
            var group = new UserGroupEntity
            {
                Id = 1,
                Name = "Group1",
                OwnerId = "owner-1",
                Members = [initialMember]
            };

            var candidate = new ApplicationUser { Id = "c1", UserName = "candidate1" };

            userGroupDataMock
                .Setup(d => d.GetUserGroupByIdAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(group);

            // Mutated condition: AddMemberAsync returns false
            userGroupDataMock
                .Setup(d => d.AddMemberAsync(1, candidate.Id, "owner-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var host = ctx.Render<DialogHost>();
            var dialogService = ctx.Services.GetRequiredService<IDialogService>();

            var parameters = new DialogParameters
            {
                [nameof(UserGroupMembersDialog.Group)] = group,
                [nameof(UserGroupMembersDialog.CurrentUserId)] = "owner-1"
            };

            _ = await dialogService.ShowAsync<UserGroupMembersDialog>("メンバー管理", parameters);

            var autocomplete = host.FindComponent<MudAutocomplete<ApplicationUser>>();
            await host.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(candidate));

            var addButton = host.FindAll("button").First(b => b.TextContent.Trim() == "追加" && !b.HasAttribute("disabled"));
            addButton.Click();

            // Verify failure behavior: Error snackbar called once, Success snackbar never called
            snackbarMock.Verify(s => s.Add("メンバーの追加に失敗しました。", Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string>()), Times.Once);
            snackbarMock.Verify(s => s.Add(It.Is<string>(msg => msg.Contains("追加しました")), Severity.Success, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string>()), Times.Never);

            // Verify member count does NOT increase to 2 (stays at 1)
            host.WaitForAssertion(() =>
            {
                Assert.Contains("メンバー一覧 (1 人)", host.Markup);
                Assert.DoesNotContain("メンバー一覧 (2 人)", host.Markup);

                // Verify candidate is not in member list
                var memberList = host.FindComponent<MudList<UserGroupMember>>();
                Assert.DoesNotContain("candidate1", memberList.Markup);
            });
        }
        finally
        {
            await ctx.DisposeAsync();
        }
    }

    [Fact]
    public async Task UserGroupMembersDialog_WhenNotOwner_AddButtonCannotBeClickedBecauseDisabled()
    {
        var ctx = CreateContext();
        try
        {
            var userGroupDataMock = new Mock<IUserGroupDataProvider>();
            var userDataMock = new Mock<IUserDataProvider>();
            var snackbarMock = new Mock<ISnackbar>();

            ctx.Services.RemoveAll<IUserGroupDataProvider>();
            ctx.Services.AddScoped(_ => userGroupDataMock.Object);
            ctx.Services.RemoveAll<IUserDataProvider>();
            ctx.Services.AddScoped(_ => userDataMock.Object);
            ctx.Services.RemoveAll<ISnackbar>();
            ctx.Services.AddScoped(_ => snackbarMock.Object);
            ctx.Services.AddTransient<UserGroupMembersViewModel>();

            var authContext = ctx.AddAuthorization();
            authContext.SetAuthorized("Member");
            authContext.SetClaims(new Claim(ClaimTypes.NameIdentifier, "non-owner-id"));

            _ = ctx.Render<MudPopoverProvider>();

            var group = new UserGroupEntity
            {
                Id = 1,
                Name = "Group1",
                OwnerId = "owner-1",
                Members = []
            };

            userGroupDataMock
                .Setup(d => d.GetUserGroupByIdAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(group);

            var host = ctx.Render<DialogHost>();
            var dialogService = ctx.Services.GetRequiredService<IDialogService>();

            var parameters = new DialogParameters
            {
                [nameof(UserGroupMembersDialog.Group)] = group,
                [nameof(UserGroupMembersDialog.CurrentUserId)] = "non-owner-id"
            };

            _ = await dialogService.ShowAsync<UserGroupMembersDialog>("メンバー管理", parameters);

            // When not owner, add section does NOT exist
            host.WaitForAssertion(() =>
            {
                Assert.DoesNotContain("新規メンバー追加", host.Markup);
                var addButtons = host.FindAll("button").Where(b => b.TextContent.Trim() == "追加");
                Assert.Empty(addButtons);
            });

            // Even if ViewModel.AddMemberAsync is called directly, it returns Failure
            var vm = ctx.Services.GetRequiredService<UserGroupMembersViewModel>();
            await vm.InitializeAsync(group, "non-owner-id");
            vm.SelectedUser = new ApplicationUser { Id = "c1", UserName = "candidate" };
            var result = await vm.AddMemberAsync();
            Assert.True(result is Failure);
            if (result is Failure failure)
            {
                Assert.Equal("メンバーを追加する権限がありません。", failure.ErrorMessage);
            }
        }
        finally
        {
            await ctx.DisposeAsync();
        }
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