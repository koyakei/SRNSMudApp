#region

using Bunit;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Admin;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

#endregion

namespace SRNSMudApp.Tests.Components.Admin;

/// <summary>
///     管理者用通報管理画面 (<see cref="ReportManager" />) の bUnit コンポーネントテスト。
/// </summary>
public sealed class ReportManagerTests : IAsyncLifetime
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IContentReportService> _reportServiceMock = new();

    public ReportManagerTests()
    {
        _ = _ctx.Services.AddAuth("admin-user-id", "Admin");
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _reportServiceMock.Object);

        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Render<MudPopoverProvider>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void ReportManager_RendersReportsTable_WithCorrectData()
    {
        var reporter = new ApplicationUser { Id = "reporter1", UserName = "reporter_alice" };
        var testReports = new List<ContentReport>
        {
            new()
            {
                Id = 101,
                TargetType = ReportTargetType.Item,
                ItemId = 1,
                TargetContentSnapshot = "不適切な投稿のテスト内容",
                Reason = "スパム・宣伝目的",
                Detail = "広告リンクです",
                Status = ReportStatus.Pending,
                OwnerId = "reporter1",
                Owner = reporter,
                CreatedDate = DateTime.UtcNow
            }
        };

        _ = _reportServiceMock.Setup(s => s.GetReportsAsync(ReportStatus.Pending, null))
            .ReturnsAsync(testReports);

        var httpContext = new DefaultHttpContext();
        IRenderedComponent<ReportManager> component =
            _ctx.Render<ReportManager>(parameters => parameters.AddCascadingValue(httpContext));

        component.WaitForState(() => component.Markup.Contains("通報管理"));

        Assert.Contains("不適切な投稿の通報管理", component.Markup);
        Assert.Contains("101", component.Markup);
        Assert.Contains("スパム・宣伝目的", component.Markup);
        Assert.Contains("reporter_alice", component.Markup);
        Assert.Contains("未対応", component.Markup);
    }

    [Fact]
    public void ReportManager_RendersEmptyMessage_WhenNoReports()
    {
        _ = _reportServiceMock.Setup(s => s.GetReportsAsync(It.IsAny<ReportStatus?>(), It.IsAny<ReportTargetType?>()))
            .ReturnsAsync([]);

        var httpContext = new DefaultHttpContext();
        IRenderedComponent<ReportManager> component =
            _ctx.Render<ReportManager>(parameters => parameters.AddCascadingValue(httpContext));

        component.WaitForState(() => component.Markup.Contains("該当する通報はありません。"));

        Assert.Contains("該当する通報はありません。", component.Markup);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}