namespace SRNSMudApp.Tests.Controllers;

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Controllers;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

using Xunit;

public class TagImportControllerTests
{
    private readonly Mock<IImportTagDataProvider> _importTagDataProviderMock = new();
    private readonly Mock<ITaggingImportDataProvider> _taggingImportDataProviderMock = new();

    private TagImportController CreateController(ClaimsPrincipal? user = null, bool includeTaggingProvider = true)
    {
        var controller = new TagImportController(
            _importTagDataProviderMock.Object,
            includeTaggingProvider ? _taggingImportDataProviderMock.Object : null);
        var httpContext = new DefaultHttpContext();
        if (user != null)
        {
            httpContext.User = user;
        }
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    [Fact]
    public async Task ImportCsv_Unauthorized_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateController();
        var request = new ImportTagCsvRequest("全て∀", "A,B,C");

        // Act
        ActionResult<ImportTagCsvResponse> result = await controller.ImportCsv(request, CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ImportCsv_EmptyParentTagName_ReturnsBadRequest(string parentTag)
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user1")], "test"));
        var controller = CreateController(user);
        var request = new ImportTagCsvRequest(parentTag, "A,B,C");

        // Act
        ActionResult<ImportTagCsvResponse> result = await controller.ImportCsv(request, CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ImportCsv_EmptyCsvContent_ReturnsBadRequest(string csv)
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user1")], "test"));
        var controller = CreateController(user);
        var request = new ImportTagCsvRequest("全て∀", csv);

        // Act
        ActionResult<ImportTagCsvResponse> result = await controller.ImportCsv(request, CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task ImportCsv_ValidRequest_CallsImportCsvTagsAsyncAndReturnsResult()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user1")], "test"));
        var controller = CreateController(user);
        var request = new ImportTagCsvRequest("全て∀", "親,子,孫");

        _importTagDataProviderMock
            .Setup(p => p.ImportCsvTagsAsync("user1", "全て∀", "親,子,孫", false))
            .ReturnsAsync(new TagImportResult(3, 1));

        // Act
        ActionResult<ImportTagCsvResponse> result = await controller.ImportCsv(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ImportTagCsvResponse>(okResult.Value);
        Assert.Equal(3, response.CreatedCount);
        Assert.Equal(1, response.UpdatedCount);
        Assert.Contains("3個のタグを新規作成", response.Message);
    }

    [Fact]
    public async Task ImportCsv_AsSystemByAdmin_PassesAsSystemTrue()
    {
        // Arrange
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "admin1"),
            new Claim(ClaimTypes.Role, "Admin")
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var controller = CreateController(user);
        var request = new ImportTagCsvRequest("全て∀", "システム,基本", AsSystem: true);

        _importTagDataProviderMock
            .Setup(p => p.ImportCsvTagsAsync("admin1", "全て∀", "システム,基本", true))
            .ReturnsAsync(new TagImportResult(2, 0));

        // Act
        ActionResult<ImportTagCsvResponse> result = await controller.ImportCsv(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ImportTagCsvResponse>(okResult.Value);
        Assert.Equal(2, response.CreatedCount);
        _importTagDataProviderMock.Verify(p => p.ImportCsvTagsAsync("admin1", "全て∀", "システム,基本", true), Times.Once);
    }

    [Fact]
    public async Task ImportCsv_AsSystemByNonAdmin_PassesAsSystemFalse()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user1")], "test"));
        var controller = CreateController(user);
        var request = new ImportTagCsvRequest("全て∀", "A,B", AsSystem: true);

        _importTagDataProviderMock
            .Setup(p => p.ImportCsvTagsAsync("user1", "全て∀", "A,B", false))
            .ReturnsAsync(new TagImportResult(2, 0));

        // Act
        ActionResult<ImportTagCsvResponse> result = await controller.ImportCsv(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        _importTagDataProviderMock.Verify(p => p.ImportCsvTagsAsync("user1", "全て∀", "A,B", false), Times.Once);
    }

    [Fact]
    public async Task ImportTagging_NoProvider_ReturnsNotImplemented()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user1")], "test"));
        var controller = CreateController(user, includeTaggingProvider: false);
        var request = new ImportTaggingJsonRequest("{}");

        // Act
        ActionResult<ImportTaggingResponse> result = await controller.ImportTagging(request, CancellationToken.None);

        // Assert
        var statusResult = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status501NotImplemented, statusResult.StatusCode);
    }

    [Fact]
    public async Task ImportTagging_Unauthorized_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateController();
        var request = new ImportTaggingJsonRequest("{}");

        // Act
        ActionResult<ImportTaggingResponse> result = await controller.ImportTagging(request, CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ImportTagging_EmptyJson_ReturnsBadRequest(string json)
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user1")], "test"));
        var controller = CreateController(user);
        var request = new ImportTaggingJsonRequest(json);

        // Act
        ActionResult<ImportTaggingResponse> result = await controller.ImportTagging(request, CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task ImportTagging_InvalidJson_ReturnsBadRequest()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user1")], "test"));
        var controller = CreateController(user);
        var request = new ImportTaggingJsonRequest("not valid json {");

        // Act
        ActionResult<ImportTaggingResponse> result = await controller.ImportTagging(request, CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task ImportTagging_ValidJson_CreatesMissingTagsAndExecutesImport()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user1")], "test"));
        var controller = CreateController(user);

        const string json = """
        {
          "TaggingRequestEntity": {
            "Item": [
              { "ItemId": "item1", "SequenceOrder": 1, "Content": "テスト本文" }
            ],
            "TagRelations": [
              {
                "RelationId": "rel1",
                "SourceItemId": "item1",
                "Tag": { "Name": "新タグ", "TagKind": "UserCustomTag" }
              }
            ],
            "TagEdges": []
          }
        }
        """;
        var request = new ImportTaggingJsonRequest(json);

        _taggingImportDataProviderMock
            .Setup(p => p.FindExistingTagAsync("新タグ", "user1", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tag?)null);

        var createdTag = new Tag { Id = 10, Name = "新タグ", OwnerId = "user1" };
        _taggingImportDataProviderMock
            .Setup(p => p.CreateTagAsync("新タグ", "user1", false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdTag);

        var expectedResult = new TaggingImportResult(1, 1, 1, 0, 0, ["完了"]);
        _taggingImportDataProviderMock
            .Setup(p => p.ExecuteImportAsync("user1", It.IsAny<TaggingImportPayload>(), It.IsAny<IReadOnlyDictionary<string, int>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        ActionResult<ImportTaggingResponse> result = await controller.ImportTagging(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ImportTaggingResponse>(okResult.Value);
        Assert.Equal(1, response.CreatedItemsCount);
        Assert.Equal(1, response.CreatedTagsCount);
        Assert.Equal(1, response.CreatedRelationsCount);
        _taggingImportDataProviderMock.Verify(p => p.CreateTagAsync("新タグ", "user1", false, null, It.IsAny<CancellationToken>()), Times.Once);
        _taggingImportDataProviderMock.Verify(p => p.ExecuteImportAsync("user1", It.IsAny<TaggingImportPayload>(), It.IsAny<IReadOnlyDictionary<string, int>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}