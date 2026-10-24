namespace SRNSMudApp.Tests.Controllers;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;

using Moq;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Controllers;
using SRNSMudApp.Services;

using Xunit;

using ClientTagLockItemDto = SRNSMudApp.Client.Models.Api.TagLockItemDto;

public class TagLockControllerTests
{
    private readonly Mock<ITagLockService> _tagLockServiceMock = new();

    private TagLockController CreateController()
    {
        return new TagLockController(_tagLockServiceMock.Object);
    }

    [Fact]
    public async Task GetLockedHierarchyLevel_ReturnsOkWithLevel()
    {
        // Arrange
        var controller = CreateController();
        _tagLockServiceMock
            .Setup(s => s.GetLockedHierarchyLevelAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        // Act
        ActionResult<int> result = await controller.GetLockedHierarchyLevel(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(3, okResult.Value);
    }

    [Fact]
    public async Task SetLockedHierarchyLevel_ValidLevel_SetsAndReturnsOk()
    {
        // Arrange
        var controller = CreateController();
        var request = new SetHierarchyLockLevelRequest(2);

        _tagLockServiceMock
            .Setup(s => s.SetLockedHierarchyLevelAsync(2, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        ActionResult<int> result = await controller.SetLockedHierarchyLevel(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(2, okResult.Value);
        _tagLockServiceMock.Verify(s => s.SetLockedHierarchyLevelAsync(2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(21)]
    public async Task SetLockedHierarchyLevel_InvalidLevel_ReturnsBadRequest(int level)
    {
        // Arrange
        var controller = CreateController();
        var request = new SetHierarchyLockLevelRequest(level);

        // Act
        ActionResult<int> result = await controller.SetLockedHierarchyLevel(request, CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
        _tagLockServiceMock.Verify(s => s.SetLockedHierarchyLevelAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAllTagsWithLockStatus_ReturnsMappedDtos()
    {
        // Arrange
        var controller = CreateController();
        var serverItems = new List<SRNSMudApp.Services.TagLockItemDto>
        {
            new(1, "全て∀", 0, null, null, null, false, false, false, false),
            new(2, "開発", 1, 1, "全て∀", "alice", true, false, false, true),
            new(3, "インフラ", 1, 1, "全て∀", "bob", false, true, false, true)
        };

        _tagLockServiceMock
            .Setup(s => s.GetAllTagsWithLockStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(serverItems);

        // Act
        ActionResult<IReadOnlyList<ClientTagLockItemDto>> result =
            await controller.GetAllTagsWithLockStatus(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dtos = Assert.IsAssignableFrom<IReadOnlyList<ClientTagLockItemDto>>(okResult.Value);
        Assert.Equal(3, dtos.Count);
        Assert.Equal("全て∀", dtos[0].Name);
        Assert.Equal(0, dtos[0].Level);
        Assert.False(dtos[0].IsDirectlyLocked);

        Assert.Equal("開発", dtos[1].Name);
        Assert.True(dtos[1].IsDirectlyLocked);
        Assert.True(dtos[1].IsLockedEffective);

        Assert.Equal("インフラ", dtos[2].Name);
        Assert.True(dtos[2].IsLevelLocked);
        Assert.True(dtos[2].IsLockedEffective);
    }

    [Fact]
    public async Task ToggleTagLock_ValidId_TogglesAndReturnsStatus()
    {
        // Arrange
        var controller = CreateController();
        _tagLockServiceMock
            .Setup(s => s.ToggleTagLockAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        ActionResult<bool> result = await controller.ToggleTagLock(10, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.True((bool)okResult.Value!);
        _tagLockServiceMock.Verify(s => s.ToggleTagLockAsync(10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task ToggleTagLock_InvalidId_ReturnsBadRequest(int invalidId)
    {
        // Arrange
        var controller = CreateController();

        // Act
        ActionResult<bool> result = await controller.ToggleTagLock(invalidId, CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
        _tagLockServiceMock.Verify(s => s.ToggleTagLockAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}