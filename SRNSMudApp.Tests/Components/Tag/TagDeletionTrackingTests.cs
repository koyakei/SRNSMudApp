using Bunit;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;

namespace SRNSMudApp.Tests.Components.Tag;

public sealed class TagDeletionTrackingTests : IAsyncLifetime
{
    private const string UserId = "tracking-user-id";

    private readonly BunitContext _ctx = new();
    private readonly Mock<IDialogLauncher> _launcherMock = new();
    private readonly Mock<IDialogReference> _addTagDialogMock = new();
    private readonly Mock<IItemCardDataProvider> _itemCardDataMock = new();
    private int _onDataChangedCount;

    public TagDeletionTrackingTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _itemCardDataMock.Object);
        _ = _ctx.Services.AddScoped(_ => _launcherMock.Object);
        _ctx.Services.AddAuthorizationCore();

        var authState = BunitTestSetup.CreateAuthState(UserId);
        Mock<AuthenticationStateProvider> authMock = new();
        _ = authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        _ctx.Services.AddScoped(_ => authMock.Object);
        _ = _ctx.Render<MudPopoverProvider>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void AddTagViaDialog_ThenCloseChip_CallsServiceMethodsAndInvokesCallback()
    {
        var ownedTag = new SRNSMudApp.Data.Tag { Id = 10, Name = "TrackingTag", OwnerId = UserId };
        var item = new SRNSMudApp.Data.Item { Id = 5, Content = "Tracking item", OwnerId = UserId };

        _ = _addTagDialogMock.Setup(r => r.Result).Returns(Task.FromResult<DialogResult?>(DialogResult.Ok(ownedTag)));
        _ = _launcherMock
            .Setup(l => l.ShowAsync(
                typeof(TagAddDialog),
                "タグの追加",
                It.IsAny<DialogParameters?>(),
                It.IsAny<DialogOptions?>()))
            .ReturnsAsync(_addTagDialogMock.Object);

        _ = _itemCardDataMock
            .Setup(d => d.GetTagWithOwnerAsync(ownedTag.Id))
            .ReturnsAsync(ownedTag);

        _ = _itemCardDataMock
            .Setup(d => d.AddFreeTagRelationAsync(item.Id, ownedTag.Id, UserId))
            .ReturnsAsync(new TagRelation { TagId = ownedTag.Id, ItemId = item.Id, OwnerId = UserId, Tag = ownedTag });

        IRenderedComponent<ItemCard> cut = RenderCard(item);

        // Act 1: 「タグを追加」ボタンでダイアログを開き、結果として新規タグを受け取る
        cut.Find("button[title='タグを追加']").Click();

        // サービス呼び出しを検証
        _itemCardDataMock.Verify(d => d.AddFreeTagRelationAsync(item.Id, ownedTag.Id, UserId), Times.Once);
        Assert.Equal(1, _onDataChangedCount);
    }

    private IRenderedComponent<ItemCard> RenderCard(SRNSMudApp.Data.Item item)
    {
        return _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, UserId)
            .Add(p => p.OnDataChanged, () =>
            {
                _onDataChangedCount++;
                return Task.CompletedTask;
            }));
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}