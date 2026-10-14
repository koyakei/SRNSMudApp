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
using SRNSMudApp.Models;
using SRNSMudApp.Services;

using Xunit;

public class LinkPreviewControllerTests
{
    private readonly Mock<ILinkPreviewService> _linkPreviewMock = new();
    private readonly Mock<IInternalLinkConversionService> _conversionMock = new();

    private LinkPreviewController CreateController(ClaimsPrincipal? user = null)
    {
        var controller = new LinkPreviewController(
            _linkPreviewMock.Object,
            _conversionMock.Object);

        if (user != null)
        {
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            };
        }

        return controller;
    }

    [Fact]
    public async Task GetPreview_EmptyUrl_ReturnsBadRequest()
    {
        // Arrange
        var controller = CreateController();

        // Act
        ActionResult<LinkPreviewDto> result =
            await controller.GetPreview(new LinkPreviewRequest("   "), CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetPreview_ValidUrl_ReturnsPreviewDto()
    {
        // Arrange
        var controller = CreateController();
        var preview = new LinkPreviewData
        {
            Url = "https://example.com/test",
            Title = "Example Title",
            Description = "Example Desc",
            ImageUrl = "https://example.com/image.png",
            SiteName = "Example",
            IsSuccess = true,
            DisplayText = null,
            Tags = [new TagPreviewItem(1, "TestTag", "Owner", 5)]
        };

        _linkPreviewMock
            .Setup(p => p.GetPreviewAsync("https://example.com/test", It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(preview);

        // Act
        ActionResult<LinkPreviewDto> result =
            await controller.GetPreview(new LinkPreviewRequest("https://example.com/test"), CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<LinkPreviewDto>(okResult.Value);
        Assert.Equal("Example Title", dto.Title);
        Assert.True(dto.IsSuccess);
        Assert.Single(dto.Tags);
        Assert.Equal("TestTag", dto.Tags[0].Name);
    }

    [Fact]
    public async Task DetectLinkCandidates_EmptyContent_ReturnsEmptyResult()
    {
        // Arrange
        var controller = CreateController();

        // Act
        ActionResult<InternalLinkConversionResultDto> result =
            await controller.DetectLinkCandidates(new DetectLinkCandidatesRequest(""), CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<InternalLinkConversionResultDto>(okResult.Value);
        Assert.Empty(dto.AutoReplaceCandidates);
        Assert.Empty(dto.ManualCandidates);
    }

    [Fact]
    public async Task DetectLinkCandidates_ValidContent_ReturnsResult()
    {
        // Arrange
        var controller = CreateController();
        var conversionResult = new InternalLinkConversionResult(
            AutoReplaceCandidates: [new LinkConversionCandidate("C#", 1, "C#", 1.0f, 0, 2, true)],
            ManualCandidates: [new LinkConversionCandidate("Rust", 2, "Rust", 0.8f, 5, 4, false)]
        );

        _conversionMock
            .Setup(c => c.DetectLinkCandidatesAsync("C# and Rust", 0.85f, It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversionResult);

        // Act
        ActionResult<InternalLinkConversionResultDto> result =
            await controller.DetectLinkCandidates(new DetectLinkCandidatesRequest("C# and Rust", 0.85f), CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<InternalLinkConversionResultDto>(okResult.Value);
        Assert.Single(dto.AutoReplaceCandidates);
        Assert.Equal("C#", dto.AutoReplaceCandidates[0].TagName);
        Assert.Single(dto.ManualCandidates);
        Assert.Equal("Rust", dto.ManualCandidates[0].TagName);
    }

    [Fact]
    public void ApplyReplacements_ValidContent_ReturnsConverted()
    {
        // Arrange
        var controller = CreateController();
        var candidates = new List<LinkConversionCandidateDto>
        {
            new("C#", 1, "C#", 1.0f, 0, 2, true)
        };

        _conversionMock
            .Setup(c => c.ApplyReplacements("C# is great", It.IsAny<IReadOnlyList<LinkConversionCandidate>>()))
            .Returns("[C#](/TagDetail/1) is great");

        // Act
        ActionResult<ApplyLinkReplacementsResponse> result =
            controller.ApplyReplacements(new ApplyLinkReplacementsRequest("C# is great", candidates));

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApplyLinkReplacementsResponse>(okResult.Value);
        Assert.Equal("[C#](/TagDetail/1) is great", response.ConvertedContent);
    }
}