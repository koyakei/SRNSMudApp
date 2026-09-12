using Moq;

using MudBlazor;

using SRNSMudApp.Components.Contract;
using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Resources;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;

namespace SRNSMudApp.Tests.Services;

/// <summary>
///     ItemCardTagCoordinator の単体テスト。
///     タグ直接付与およびコントラクト提案モーダル呼び出しフローを検証する。
/// </summary>
public class ItemCardTagCoordinatorTests
{
    private const string UserId = "tag-user-1";

    private readonly Mock<IItemCardDataProvider> _itemCardDataMock = new();
    private readonly Mock<IDialogLauncher> _dialogLauncherMock = new();
    private readonly Mock<IDialogReference> _dialogReferenceMock = new();
    private readonly Mock<ISnackbar> _snackbarMock = new();
    private readonly ItemCardTagCoordinator _coordinator;

    public ItemCardTagCoordinatorTests()
    {
        _coordinator = new ItemCardTagCoordinator(
            _itemCardDataMock.Object,
            _dialogLauncherMock.Object,
            _snackbarMock.Object);
    }

    [Fact]
    public void Constructor_NullParameters_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ItemCardTagCoordinator(null!, _dialogLauncherMock.Object, _snackbarMock.Object));
        Assert.Throws<ArgumentNullException>(() => new ItemCardTagCoordinator(_itemCardDataMock.Object, null!, _snackbarMock.Object));
        Assert.Throws<ArgumentNullException>(() => new ItemCardTagCoordinator(_itemCardDataMock.Object, _dialogLauncherMock.Object, null!));
    }

    [Fact]
    public async Task PromptAndAddTagAsync_WhenDialogCanceled_ReturnsNone()
    {
        var item = new Item { Id = 1, OwnerId = UserId, TagRelations = [] };

        _ = _dialogReferenceMock.Setup(r => r.Result).ReturnsAsync(DialogResult.Cancel());
        _ = _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(TagAddDialog), "タグの追加", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(_dialogReferenceMock.Object);

        var outcome = await _coordinator.PromptAndAddTagAsync(item, UserId);

        Assert.Equal(TagAddOutcome.None, outcome);
    }

    [Fact]
    public async Task PromptAndAddTagAsync_WhenTagAlreadyOnItem_ShowsWarningAndReturnsNone()
    {
        var selectedTag = new Tag { Id = 10, Name = "ExistingTag", OwnerId = UserId };
        var item = new Item
        {
            Id = 1,
            OwnerId = UserId,
            TagRelations = [new TagRelation { TagId = 10, ItemId = 1, OwnerId = UserId }]
        };

        _ = _dialogReferenceMock.Setup(r => r.Result).ReturnsAsync(DialogResult.Ok(selectedTag));
        _ = _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(TagAddDialog), "タグの追加", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(_dialogReferenceMock.Object);

        _ = _itemCardDataMock.Setup(d => d.GetTagWithOwnerAsync(10)).ReturnsAsync(selectedTag);
        _ = _itemCardDataMock.Setup(d => d.CanUserAttachTagDirectlyAsync(10, UserId)).ReturnsAsync(true);

        var outcome = await _coordinator.PromptAndAddTagAsync(item, UserId);

        Assert.Equal(TagAddOutcome.None, outcome);
        _snackbarMock.Verify(s => s.Add(ErrorMessages.TagAlreadyAdded, Severity.Warning, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task PromptAndAddTagAsync_WhenCanAttachDirectly_AddsRelationAndReturnsAddedDirectly()
    {
        var selectedTag = new Tag { Id = 20, Name = "NewTag", OwnerId = UserId };
        var item = new Item { Id = 1, OwnerId = UserId, TagRelations = [] };
        var createdRelation = new TagRelation { Id = 5, ItemId = 1, TagId = 20, OwnerId = UserId };

        _ = _dialogReferenceMock.Setup(r => r.Result).ReturnsAsync(DialogResult.Ok(selectedTag));
        _ = _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(TagAddDialog), "タグの追加", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(_dialogReferenceMock.Object);

        _ = _itemCardDataMock.Setup(d => d.GetTagWithOwnerAsync(20)).ReturnsAsync(selectedTag);
        _ = _itemCardDataMock.Setup(d => d.CanUserAttachTagDirectlyAsync(20, UserId)).ReturnsAsync(true);
        _ = _itemCardDataMock.Setup(d => d.AddFreeTagRelationAsync(1, 20, UserId)).ReturnsAsync(createdRelation);

        var outcome = await _coordinator.PromptAndAddTagAsync(item, UserId);

        Assert.Equal(TagAddOutcome.AddedDirectly, outcome);
        Assert.Single(item.TagRelations);
        Assert.Equal(selectedTag, item.TagRelations.Single().Tag);
        _snackbarMock.Verify(s => s.Add("タグを追加しました。", Severity.Success, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task PromptAndAddTagAsync_WhenCannotAttachDirectly_OpensProposeContractDialog()
    {
        var selectedTag = new Tag { Id = 30, Name = "ProtectedTag", OwnerId = "other" };
        var item = new Item { Id = 1, OwnerId = UserId, TagRelations = [] };

        var tagAddDialogRef = new Mock<IDialogReference>();
        tagAddDialogRef.Setup(r => r.Result).ReturnsAsync(DialogResult.Ok(selectedTag));
        _ = _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(TagAddDialog), "タグの追加", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(tagAddDialogRef.Object);

        var proposeDialogRef = new Mock<IDialogReference>();
        proposeDialogRef.Setup(r => r.Result).ReturnsAsync(DialogResult.Ok<object?>(null));
        _ = _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(ProposeContractDialog), "コントラクトの提案", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(proposeDialogRef.Object);

        _ = _itemCardDataMock.Setup(d => d.GetTagWithOwnerAsync(30)).ReturnsAsync(selectedTag);
        _ = _itemCardDataMock.Setup(d => d.CanUserAttachTagDirectlyAsync(30, UserId)).ReturnsAsync(false);

        var outcome = await _coordinator.PromptAndAddTagAsync(item, UserId);

        Assert.Equal(TagAddOutcome.ContractProposed, outcome);
        _dialogLauncherMock.Verify(l => l.ShowAsync(typeof(ProposeContractDialog), "コントラクトの提案", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()), Times.Once);
    }
}