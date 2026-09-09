using System.Security.Claims;

using Bunit;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Contract;
using SRNSMudApp.Components.Tag;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;
using SRNSMudApp.Tests.TestSupport;

namespace SRNSMudApp.Tests.Components.UI;

using Tag = SRNSMudApp.Data.Tag;

/// <summary>
///     ItemCard におけるタグ追加時、自動承認が有効なタグはコントラクト提案モーダルを開かずに
///     直接追加され、自動承認が無効なタグは提案モーダルが開かれることを検証するテスト。
/// </summary>
public sealed class ItemCardTagAddTests : IAsyncLifetime
{
    private const string UserId = "test-user-1";
    private readonly BunitContext _ctx = new();
    private readonly Mock<IDialogLauncher> _dialogLauncherMock = new();
    private readonly Mock<IDialogReference> _tagAddDialogRefMock = new();
    private readonly Mock<IDialogReference> _proposeDialogRefMock = new();
    private readonly Mock<IItemCardDataProvider> _itemCardDataMock = new();
    private readonly Mock<IItemTagService> _itemTagServiceMock = new();

    public ItemCardTagAddTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ctx.Services.AddAuthorizationCore();

        AuthenticationState authState = BunitTestSetup.CreateAuthState(UserId);
        Mock<AuthenticationStateProvider> authMock = new();
        _ = authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        _ctx.Services.AddScoped(_ => authMock.Object);

        _ctx.Services.RemoveAll<IDialogLauncher>();
        _ctx.Services.AddScoped(_ => _dialogLauncherMock.Object);

        _ctx.Services.RemoveAll<IItemCardDataProvider>();
        _ctx.Services.AddScoped(_ => _itemCardDataMock.Object);

        _ctx.Services.RemoveAll<IItemTagService>();
        _ctx.Services.AddScoped(_ => _itemTagServiceMock.Object);

        _ctx.Render<MudPopoverProvider>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public async Task WhenAddingAutoApprovedTag_DoesNotShowProposeContractDialog_AndAddsTagDirectly()
    {
        var item = new SRNSMudApp.Data.Item
        {
            Id = 50,
            Content = "Test Item for Auto Approve",
            OwnerId = UserId,
            TagRelations = []
        };

        var selectedTag = new Tag
        {
            Id = 101,
            Name = "AutoApprovedTag",
            OwnerId = "other-user",
            AutoAcceptIncomingTaggingRequests = true
        };

        _tagAddDialogRefMock.Setup(r => r.Result).ReturnsAsync(DialogResult.Ok(selectedTag));
        _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(TagAddDialog), "タグの追加", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(_tagAddDialogRefMock.Object);

        _itemCardDataMock
            .Setup(d => d.GetTagWithOwnerAsync(selectedTag.Id))
            .ReturnsAsync(selectedTag);

        _itemCardDataMock
            .Setup(d => d.CanUserAttachTagDirectlyAsync(selectedTag.Id, UserId))
            .ReturnsAsync(true);

        _itemCardDataMock
            .Setup(d => d.AddFreeTagRelationAsync(item.Id, selectedTag.Id, UserId))
            .ReturnsAsync(new TagRelation
            {
                Id = 1,
                ItemId = item.Id,
                TagId = selectedTag.Id,
                OwnerId = UserId,
                Tag = selectedTag,
                Weight = 1
            });

        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, UserId));

        // タグ追加ボタンをクリック
        var addTagButton = cut.Find("button[title='タグを追加']");
        await cut.InvokeAsync(() => addTagButton.Click());

        // TagAddDialog は開かれた
        _dialogLauncherMock.Verify(
            l => l.ShowAsync(typeof(TagAddDialog), "タグの追加", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()),
            Times.Once);

        // ProposeContractDialog は開かれなかったこと（追加リクエストモーダルが立ち上がらないこと）を検証
        _dialogLauncherMock.Verify(
            l => l.ShowAsync(typeof(ProposeContractDialog), It.IsAny<string>(), It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()),
            Times.Never);

        // 直接タグが付与されたことを検証
        _itemCardDataMock.Verify(
            d => d.AddFreeTagRelationAsync(item.Id, selectedTag.Id, UserId),
            Times.Once);

        // Item の TagRelations にタグが追加されたことを検証
        Assert.Contains(item.TagRelations, tr => tr.TagId == selectedTag.Id);
    }

    [Fact]
    public async Task WhenAddingNonAutoApprovedTag_ShowsProposeContractDialog()
    {
        var item = new SRNSMudApp.Data.Item
        {
            Id = 51,
            Content = "Test Item for Non Auto Approve",
            OwnerId = UserId,
            TagRelations = []
        };

        var selectedTag = new Tag
        {
            Id = 102,
            Name = "NonAutoApprovedTag",
            OwnerId = "other-user",
            AutoAcceptIncomingTaggingRequests = false
        };

        _tagAddDialogRefMock.Setup(r => r.Result).ReturnsAsync(DialogResult.Ok(selectedTag));
        _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(TagAddDialog), "タグの追加", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(_tagAddDialogRefMock.Object);

        _proposeDialogRefMock.Setup(r => r.Result).ReturnsAsync(DialogResult.Cancel());
        _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(ProposeContractDialog), "コントラクトの提案", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(_proposeDialogRefMock.Object);

        _itemCardDataMock
            .Setup(d => d.GetTagWithOwnerAsync(selectedTag.Id))
            .ReturnsAsync(selectedTag);

        _itemCardDataMock
            .Setup(d => d.CanUserAttachTagDirectlyAsync(selectedTag.Id, UserId))
            .ReturnsAsync(false);

        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, UserId));

        // タグ追加ボタンをクリック
        var addTagButton = cut.Find("button[title='タグを追加']");
        await cut.InvokeAsync(() => addTagButton.Click());

        // ProposeContractDialog が開かれたこと（リクエストモーダルが立ち上がること）を検証
        _dialogLauncherMock.Verify(
            l => l.ShowAsync(typeof(ProposeContractDialog), "コントラクトの提案", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()),
            Times.Once);

        // 直接タグ付与は呼ばれなかったことを検証
        _itemCardDataMock.Verify(
            d => d.AddFreeTagRelationAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()),
            Times.Never);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}