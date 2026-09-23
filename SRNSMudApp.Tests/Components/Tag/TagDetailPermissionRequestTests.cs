using AngleSharp.Dom;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Contract;
using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;
using SRNSMudApp.Tests.TestSupport;

using Tag = SRNSMudApp.Data.Tag;

namespace SRNSMudApp.Tests.Components.Tag;

public sealed class TagDetailPermissionRequestTests : IAsyncLifetime
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<ITagDetailDataProvider> _tagDetailDataMock = new();
    private readonly Mock<IDialogLauncher> _dialogLauncherMock = new();
    private readonly Mock<ITagContentProposalService> _contentProposalMock = new();
    private readonly Mock<ITagNameProposalService> _nameProposalMock = new();

    public TagDetailPermissionRequestTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices();
        _ = _ctx.Services.AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _tagDetailDataMock.Object);
        _ = _ctx.Services.AddScoped(_ => _dialogLauncherMock.Object);
        _ = _ctx.Services.AddScoped(_ => _contentProposalMock.Object);
        _ = _ctx.Services.AddScoped(_ => _nameProposalMock.Object);
        _ = _ctx.Services.AddAuthorizationCore();
        _ = _ctx.Services.AddAuth("default-user");
        _ = _ctx.Render<MudPopoverProvider>();

        _contentProposalMock.Setup(s => s.GetPendingProposalsForTagAsync(It.IsAny<int>()))
            .ReturnsAsync([]);
        _nameProposalMock.Setup(s => s.GetPendingProposalsForTagAsync(It.IsAny<int>()))
            .ReturnsAsync([]);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    private IRenderedComponent<TagDetail> RenderTagDetail(int tagId, string userId)
    {
        return _ctx.Render<TagDetail>(parameters => parameters
            .Add(p => p.TagId, tagId)
            .AddCascadingValue(Task.FromResult(BunitTestSetup.CreateAuthState(userId))));
    }

    [Fact]
    public void WhenNonOwnerUserViewsTagDetail_PermissionRequestButtonsAreVisible()
    {
        var tag = new SRNSMudApp.Data.Tag
        {
            Id = 42,
            Name = "サンプルタグ",
            OwnerId = "tag-owner",
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        var pageData = new TagDetailPageData(
            tag,
            false,
            [],
            [],
            [],
            [],
            []);

        _tagDetailDataMock.Setup(d => d.GetTagDetailAsync(42, "user-viewer"))
            .ReturnsAsync(pageData);

        IRenderedComponent<TagDetail> cut = RenderTagDetail(42, "user-viewer");

        cut.WaitForState(() => cut.Markup.Contains("サンプルタグ"));

        // ヘッダーおよびリクエストタブの両方に「操作権限をリクエスト」ボタンが表示されること
        List<IElement> buttons = cut.FindAll("button")
            .Where(b => b.TextContent.Contains("操作権限をリクエスト"))
            .ToList();

        Assert.NotEmpty(buttons);
    }

    [Fact]
    public void WhenOwnerUserViewsTagDetail_PermissionRequestButtonsAreNotVisible()
    {
        var tag = new SRNSMudApp.Data.Tag
        {
            Id = 42,
            Name = "サンプルタグ",
            OwnerId = "tag-owner",
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        var pageData = new TagDetailPageData(
            tag,
            false,
            [],
            [],
            [],
            [],
            []);

        _tagDetailDataMock.Setup(d => d.GetTagDetailAsync(42, "tag-owner"))
            .ReturnsAsync(pageData);

        IRenderedComponent<TagDetail> cut = RenderTagDetail(42, "tag-owner");

        cut.WaitForState(() => cut.Markup.Contains("サンプルタグ"));

        // オーナーには「操作権限をリクエスト」ボタンが表示されないこと
        List<IElement> buttons = cut.FindAll("button")
            .Where(b => b.TextContent.Contains("操作権限をリクエスト"))
            .ToList();

        Assert.Empty(buttons);
    }

    [Fact]
    public async Task WhenClickPermissionRequestButton_OpensRequestTagPermissionDialog()
    {
        var tag = new SRNSMudApp.Data.Tag
        {
            Id = 42,
            Name = "サンプルタグ",
            OwnerId = "tag-owner",
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        var pageData = new TagDetailPageData(
            tag,
            false,
            [],
            [],
            [],
            [],
            []);

        _tagDetailDataMock.Setup(d => d.GetTagDetailAsync(42, "user-viewer"))
            .ReturnsAsync(pageData);

        var dialogRefMock = new Mock<IDialogReference>();
        dialogRefMock.Setup(d => d.Result).ReturnsAsync(DialogResult.Cancel());

        _dialogLauncherMock
            .Setup(l => l.ShowAsync(
                typeof(RequestTagPermissionDialog),
                "操作権限のリクエスト",
                It.IsAny<DialogParameters?>(),
                It.IsAny<DialogOptions?>()))
            .ReturnsAsync(dialogRefMock.Object);

        IRenderedComponent<TagDetail> cut = RenderTagDetail(42, "user-viewer");

        cut.WaitForState(() => cut.Markup.Contains("サンプルタグ"));

        IElement requestButton = cut.FindAll("button")
            .First(b => b.TextContent.Contains("操作権限をリクエスト"));

        await cut.InvokeAsync(() => requestButton.Click());

        _dialogLauncherMock.Verify(l => l.ShowAsync(
            typeof(RequestTagPermissionDialog),
            "操作権限のリクエスト",
            It.IsAny<DialogParameters?>(),
            It.IsAny<DialogOptions?>()), Times.Once);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}