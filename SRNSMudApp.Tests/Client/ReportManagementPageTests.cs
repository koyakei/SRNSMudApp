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

public class ReportManagementPageTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemListApiClient> _apiClientMock = new();

    public ReportManagementPageTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddScoped(_ => _apiClientMock.Object);
        _ctx.Render<MudPopoverProvider>();
    }

    [Fact]
    public void WhenInitialized_LoadsReportsAndRendersTable()
    {
        // Arrange
        var reports = new List<ContentReportDto>
        {
            new(
                Id: 101,
                TargetType: "Item",
                ItemId: 1,
                TagId: null,
                TargetContentSnapshot: "不適切なコンテンツ本文",
                Reason: "Spam",
                Detail: "明らかなスパム投稿です",
                Status: "Pending",
                ResolutionNote: null,
                ReporterUserId: "u1",
                ReporterUserName: "Alice",
                HandledByAdminId: null,
                HandledByAdminName: null,
                CreatedDate: DateTime.UtcNow,
                HandledDate: null
            )
        };

        _apiClientMock
            .Setup(c => c.GetReportsAsync("Pending", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reports);

        // Act
        var cut = _ctx.Render<ReportManagementPage>();

        // Assert
        Assert.Contains("通報管理 (WASM)", cut.Markup);
        Assert.Contains("#101", cut.Markup);
        Assert.Contains("不適切なコンテンツ本文", cut.Markup);
        Assert.Contains("Spam", cut.Markup);
        Assert.Contains("Alice", cut.Markup);
        Assert.Contains("未対応", cut.Markup);
    }

    [Fact]
    public void WhenDetailClicked_ShowsActionCard()
    {
        // Arrange
        var reports = new List<ContentReportDto>
        {
            new(
                Id: 102,
                TargetType: "Tag",
                ItemId: null,
                TagId: 20,
                TargetContentSnapshot: "暴言タグ名",
                Reason: "Abuse",
                Detail: "暴言が含まれています",
                Status: "Pending",
                ResolutionNote: null,
                ReporterUserId: "u2",
                ReporterUserName: "Bob",
                HandledByAdminId: null,
                HandledByAdminName: null,
                CreatedDate: DateTime.UtcNow,
                HandledDate: null
            )
        };

        _apiClientMock
            .Setup(c => c.GetReportsAsync("Pending", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reports);

        var cut = _ctx.Render<ReportManagementPage>();

        // Act - 詳細・対応ボタンをクリック
        var detailBtn = cut.Find("[data-testid='report-detail-btn-102']");
        detailBtn.Click();

        // Assert
        Assert.Contains("通報 #102 の詳細・対応", cut.Markup);
        Assert.Contains("暴言タグ名", cut.Markup);
        Assert.Contains("暴言が含まれています", cut.Markup);
    }

    [Fact]
    public void WhenReviewClicked_CallsUpdateStatusAndReloads()
    {
        // Arrange
        var reports = new List<ContentReportDto>
        {
            new(
                Id: 103,
                TargetType: "Item",
                ItemId: 5,
                TagId: null,
                TargetContentSnapshot: "コンテンツ",
                Reason: "Other",
                Detail: null,
                Status: "Pending",
                ResolutionNote: null,
                ReporterUserId: "u3",
                ReporterUserName: "Charlie",
                HandledByAdminId: null,
                HandledByAdminName: null,
                CreatedDate: DateTime.UtcNow,
                HandledDate: null
            )
        };

        _apiClientMock
            .Setup(c => c.GetReportsAsync("Pending", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reports);

        _apiClientMock
            .Setup(c => c.UpdateReportStatusAsync(103, It.IsAny<UpdateReportStatusRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var cut = _ctx.Render<ReportManagementPage>();

        // 詳細を開く
        var detailBtn = cut.Find("[data-testid='report-detail-btn-103']");
        detailBtn.Click();

        // Act - 「確認済みにする」をクリック
        var reviewBtn = cut.Find("[data-testid='review-report-btn']");
        reviewBtn.Click();

        // Assert
        _apiClientMock.Verify(c => c.UpdateReportStatusAsync(103, It.Is<UpdateReportStatusRequest>(r => r.Status == "Reviewed"), It.IsAny<CancellationToken>()), Times.Once);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}