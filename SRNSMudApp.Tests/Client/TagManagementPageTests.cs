namespace SRNSMudApp.Tests.Client;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Client.Pages;
using SRNSMudApp.Client.Services;

using Xunit;

public class TagManagementPageTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemListApiClient> _apiClientMock = new();

    public TagManagementPageTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddScoped(_ => _apiClientMock.Object);
        _ctx.Render<MudPopoverProvider>();
    }

    [Fact]
    public void WhenInitialized_LoadsDataAndRendersLevelAndTags()
    {
        // Arrange
        _apiClientMock
            .Setup(c => c.GetTagLockLevelAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var tags = new List<TagLockItemDto>
        {
            new(1, "全て∀", 0, null, null, null, false, false, false, false),
            new(2, "開発", 1, 1, "全て∀", "alice", true, false, false, true),
            new(3, "インフラ", 1, 1, "全て∀", "bob", false, true, false, true)
        };

        _apiClientMock
            .Setup(c => c.GetTagLockItemsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(tags);

        // Act
        var cut = _ctx.Render<TagManagementPage>();

        // Assert
        Assert.Contains("タグ管理 (WASM)", cut.Markup);
        Assert.Contains("第3 階層までロック中", cut.Markup);
        Assert.Contains("全て∀", cut.Markup);
        Assert.Contains("開発", cut.Markup);
        Assert.Contains("インフラ", cut.Markup);
        Assert.Contains("個別ロック", cut.Markup);
        Assert.Contains("階層ロック", cut.Markup);
    }

    [Fact]
    public async Task WhenSaveHierarchyLockSettingClicked_CallsSetTagLockLevelAsync()
    {
        // Arrange
        _apiClientMock
            .Setup(c => c.GetTagLockLevelAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _apiClientMock
            .Setup(c => c.GetTagLockItemsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _apiClientMock
            .Setup(c => c.SetTagLockLevelAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var cut = _ctx.Render<TagManagementPage>();

        // 数値フィールドを変更
        var numericField = cut.FindComponent<MudNumericField<int>>();
        await cut.InvokeAsync(() => numericField.Instance.ValueChanged.InvokeAsync(2));

        // Act
        var saveBtn = cut.Find("[data-testid='save-lock-level-btn']");
        saveBtn.Click();

        // Assert
        _apiClientMock.Verify(c => c.SetTagLockLevelAsync(2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void WhenToggleTagLockClicked_CallsToggleTagLockAsync()
    {
        // Arrange
        _apiClientMock
            .Setup(c => c.GetTagLockLevelAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var tags = new List<TagLockItemDto>
        {
            new(10, "テストタグ", 1, 1, "全て∀", "user1", false, false, false, false)
        };

        _apiClientMock
            .Setup(c => c.GetTagLockItemsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(tags);

        _apiClientMock
            .Setup(c => c.ToggleTagLockAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var cut = _ctx.Render<TagManagementPage>();

        // Act
        var toggleBtn = cut.Find("[data-testid='toggle-lock-10']");
        toggleBtn.Click();

        // Assert
        _apiClientMock.Verify(c => c.ToggleTagLockAsync(10, It.IsAny<CancellationToken>()), Times.Once);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}