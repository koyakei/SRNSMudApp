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
using SRNSMudApp.Services;

using Xunit;

public class ReportControllerTests
{
    private readonly Mock<IContentReportService> _contentReportServiceMock = new();

    private ReportController CreateController(ClaimsPrincipal? user = null)
    {
        var controller = new ReportController(_contentReportServiceMock.Object);
        var httpContext = new DefaultHttpContext();
        if (user != null)
        {
            httpContext.User = user;
        }
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    [Fact]
    public async Task CreateReport_Unauthorized_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateController(); // 未認証
        var request = new CreateReportRequest("Item", 1, null, "Spam", "Detail");

        // Act
        ActionResult<ContentReportDto> result = await controller.CreateReport(request, CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateReport_Authorized_CreatesAndReturnsReport()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "reporter-1")], "test"));
        var controller = CreateController(user);
        var request = new CreateReportRequest("Item", 10, null, "Spam", "Test Detail");

        var createdReport = new ContentReport
        {
            Id = 1,
            TargetType = ReportTargetType.Item,
            ItemId = 10,
            TargetContentSnapshot = "Item Content Snapshot",
            Reason = "Spam",
            Detail = "Test Detail",
            OwnerId = "reporter-1"
        };

        _contentReportServiceMock
            .Setup(s => s.CreateReportAsync(It.IsAny<CreateContentReportDto>(), "reporter-1"))
            .ReturnsAsync(createdReport);

        // Act
        ActionResult<ContentReportDto> result = await controller.CreateReport(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ContentReportDto>(okResult.Value);
        Assert.Equal(1, dto.Id);
        Assert.Equal("Item", dto.TargetType);
        Assert.Equal("Spam", dto.Reason);
        Assert.Equal("reporter-1", dto.ReporterUserId);
    }

    [Fact]
    public async Task GetReports_ReturnsFilteredReports()
    {
        // Arrange
        var controller = CreateController();
        var reports = new List<ContentReport>
        {
            new()
            {
                Id = 1,
                TargetType = ReportTargetType.Item,
                ItemId = 1,
                TargetContentSnapshot = "Snapshot 1",
                Reason = "Abuse",
                OwnerId = "user-1"
            }
        };

        _contentReportServiceMock
            .Setup(s => s.GetReportsAsync(ReportStatus.Pending, ReportTargetType.Item))
            .ReturnsAsync(reports);

        // Act
        ActionResult<IReadOnlyList<ContentReportDto>> result =
            await controller.GetReports("Pending", "Item", CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dtos = Assert.IsAssignableFrom<IReadOnlyList<ContentReportDto>>(okResult.Value);
        Assert.Single(dtos);
        Assert.Equal(1, dtos[0].Id);
        Assert.Equal("Abuse", dtos[0].Reason);
    }

    [Fact]
    public async Task GetReportById_NotFound_ReturnsNotFound()
    {
        // Arrange
        var controller = CreateController();
        _contentReportServiceMock
            .Setup(s => s.GetReportByIdAsync(999))
            .ReturnsAsync((ContentReport?)null);

        // Act
        ActionResult<ContentReportDto> result = await controller.GetReportById(999, CancellationToken.None);

        // Assert
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetReportById_Found_ReturnsReport()
    {
        // Arrange
        var controller = CreateController();
        var report = new ContentReport
        {
            Id = 5,
            TargetType = ReportTargetType.Tag,
            TagId = 20,
            TargetContentSnapshot = "Tag Name",
            Reason = "Inappropriate",
            OwnerId = "user-2"
        };
        _contentReportServiceMock
            .Setup(s => s.GetReportByIdAsync(5))
            .ReturnsAsync(report);

        // Act
        ActionResult<ContentReportDto> result = await controller.GetReportById(5, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ContentReportDto>(okResult.Value);
        Assert.Equal(5, dto.Id);
        Assert.Equal("Tag", dto.TargetType);
    }

    [Fact]
    public async Task UpdateReportStatus_Success_ReturnsTrue()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "admin-1")], "test"));
        var controller = CreateController(user);
        var request = new UpdateReportStatusRequest("Reviewed", "Confirmed okay");

        _contentReportServiceMock
            .Setup(s => s.UpdateReportStatusAsync(1, ReportStatus.Reviewed, "Confirmed okay", "admin-1"))
            .ReturnsAsync(true);

        // Act
        ActionResult<bool> result = await controller.UpdateReportStatus(1, request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.True((bool)okResult.Value!);
    }

    [Fact]
    public async Task ResolveReport_Hide_CallsResolveWithHide()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "admin-1")], "test"));
        var controller = CreateController(user);
        var request = new ResolveReportRequest(DeleteTarget: false, HideTarget: true, ResolutionNote: "Hidden content");

        _contentReportServiceMock
            .Setup(s => s.ResolveReportWithHideAsync(1, "Hidden content", "admin-1"))
            .ReturnsAsync(true);

        // Act
        ActionResult<bool> result = await controller.ResolveReport(1, request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.True((bool)okResult.Value!);
    }

    [Fact]
    public async Task ResolveReport_Delete_CallsResolveWithAction()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "admin-1")], "test"));
        var controller = CreateController(user);
        var request = new ResolveReportRequest(DeleteTarget: true, HideTarget: false, ResolutionNote: "Deleted spam");

        _contentReportServiceMock
            .Setup(s => s.ResolveReportWithActionAsync(1, true, "Deleted spam", "admin-1"))
            .ReturnsAsync(true);

        // Act
        ActionResult<bool> result = await controller.ResolveReport(1, request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.True((bool)okResult.Value!);
    }
}