using Bunit;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Contract;
using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;

namespace SRNSMudApp.Tests.Components.Tag;

public sealed class ItemTagChipTests : IAsyncLifetime
{
    private readonly BunitContext _ctx = new();

    public ItemTagChipTests()
    {
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddAuth("test-user-id");
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Render<MudPopoverProvider>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void ItemTagChip_ShouldRenderTagNameAndWeight_ForTagRelation()
    {
        var tag = new SRNSMudApp.Data.Tag { Id = 1, Name = "TestTag", OwnerId = "test-user-id" };
        var tagRelation = new TagRelation
        {
            Id = 1,
            TagId = 1,
            Tag = tag,
            ItemId = 1,
            Weight = 10,
            OwnerId = "test-user-id"
        };
        var item = new SRNSMudApp.Data.Item { Id = 1, Content = "Test Item", OwnerId = "test-user-id" };

        IRenderedComponent<ItemTagChip> component = _ctx.Render<ItemTagChip>(parameters => parameters
            .Add(p => p.TagRelation, tagRelation)
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, "test-user-id")
        );

        Assert.Contains("TestTag", component.Markup);
        Assert.Contains("10", component.Markup);
        Assert.Contains("mud-icon", component.Markup);
    }

    [Fact]
    public async Task ItemTagChip_WhenTreeButtonClicked_ScrollsCurrentTagIntoView()
    {
        var tag = new SRNSMudApp.Data.Tag { Id = 1, Name = "TestTag", OwnerId = "test-user-id" };
        var tagRelation = new TagRelation
        {
            Id = 1,
            TagId = 1,
            Tag = tag,
            ItemId = 1,
            Weight = 10,
            OwnerId = "test-user-id"
        };
        var item = new SRNSMudApp.Data.Item { Id = 1, Content = "Test Item", OwnerId = "test-user-id" };

        _ctx.JSInterop.SetupVoid("contentOverflowHelper.scrollToElement", _ => true);

        IRenderedComponent<ItemTagChip> component = _ctx.Render<ItemTagChip>(parameters => parameters
            .Add(p => p.TagRelation, tagRelation)
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, "test-user-id")
            .Add(p => p.AllTags, new[] { tag })
            .Add(p => p.AllTagRelationsToTags, Array.Empty<TagRelationToTag>())
        );

        var treeButton = component.FindAll("button")
            .First(b => b.GetAttribute("title") == "タグツリーを表示");
        await component.InvokeAsync(() => treeButton.Click());

        component.WaitForAssertion(() => _ctx.JSInterop.VerifyInvoke("contentOverflowHelper.scrollToElement"));
    }

    /// <summary>
    ///     systemがownerのタグを一般ユーザーAが付与したTagRelationに対し、
    ///     一般ユーザーBが削除（閉じる）ボタンをクリックした場合、
    ///     直接解除（ItemTagService.RemoveTagRelationAsync）は呼び出されず、
    ///     コントラクト提案ダイアログ（ProposeContractDialog）が表示されることを検証する。
    /// </summary>
    [Fact]
    public async Task ItemTagChip_WhenTagRelationOwnedByUserAButUserBAttemptsRemoval_DoesNotRemoveDirectlyAndOpensContractDialog()
    {
        var mockItemTagService = Mock.Get(_ctx.Services.GetRequiredService<IItemTagService>());
        var mockDialogLauncher = Mock.Get(_ctx.Services.GetRequiredService<IDialogLauncher>());

        var mockDialogReference = new Mock<IDialogReference>();
        _ = mockDialogReference.Setup(d => d.Result).ReturnsAsync(DialogResult.Cancel());
        _ = mockDialogLauncher
            .Setup(l => l.ShowAsync(
                typeof(ProposeContractDialog),
                "コントラクトの提案",
                It.IsAny<DialogParameters>(),
                It.IsAny<DialogOptions>()))
            .ReturnsAsync(mockDialogReference.Object);

        var systemTag = new SRNSMudApp.Data.Tag { Id = 1, Name = "SystemClassificationTag", OwnerId = "system", IsSystem = true };
        var tagRelation = new TagRelation
        {
            Id = 10,
            TagId = 1,
            Tag = systemTag,
            ItemId = 100,
            Weight = 1,
            OwnerId = "user-a" // 一般ユーザーAが付与
        };
        var item = new SRNSMudApp.Data.Item { Id = 100, Content = "Test Item", OwnerId = "user-a" };

        IRenderedComponent<ItemTagChip> component = _ctx.Render<ItemTagChip>(parameters => parameters
            .Add(p => p.TagRelation, tagRelation)
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, "user-b") // 一般ユーザーBとして操作
            .Add(p => p.AllTags, new[] { systemTag })
        );

        // 「関連付けを削除」ボタンをクリック
        var removeButton = component.FindAll("button")
            .First(b => b.GetAttribute("title") == "関連付けを削除");
        await component.InvokeAsync(() => removeButton.Click());

        // 直接の解除処理（RemoveTagRelationAsync）は絶対に呼ばれないことを検証
        mockItemTagService.Verify(
            s => s.RemoveTagRelationAsync(It.IsAny<int>(), It.IsAny<string>()),
            Times.Never);

        // 代わりにコントラクト提案ダイアログが表示されることを検証
        mockDialogLauncher.Verify(
            l => l.ShowAsync(
                typeof(ProposeContractDialog),
                "コントラクトの提案",
                It.IsAny<DialogParameters>(),
                It.IsAny<DialogOptions>()),
            Times.Once);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}