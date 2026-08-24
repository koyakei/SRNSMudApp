using System;
using System.Linq;
using System.Threading.Tasks;

using Bunit;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

namespace SRNSMudApp.Tests.Components.Tag;

/// <summary>
///     TaggingRequestList がダイアログを <see cref="IDialogLauncher" /> 経由で起動することを検証する。
///     具象ダイアログへの依存は「起動時に渡される型 + タイトル + オプション」の検証のみに限定する。
/// </summary>
public class TaggingRequestListDialogLauncherTests : IAsyncDisposable
{
    private const string TagOwnerId = "tag-owner";

    private readonly BunitContext _ctx = new();
    private readonly Mock<IDialogLauncher> _launcherMock = new();
    private readonly Mock<IDialogReference> _referenceMock = new();

    public TaggingRequestListDialogLauncherTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices();

        // IDialogService ではなく IDialogLauncher のモックを注入して起動を検証する
        _ctx.Services.RemoveAll<IDialogLauncher>();
        _ctx.Services.AddSingleton(_ => _launcherMock.Object);
        _ctx.Services.AddSingleton<ITaggingRequestActions>(
            new TaggingRequestActions(null!, new Mock<ITaggingService>().Object, _launcherMock.Object, new Mock<ISnackbar>().Object));
    }

    [Fact]
    public void ClickRejectButton_LaunchesRejectDialogViaLauncher_WithExpectedTypeAndOptions()
    {
        var canceled = DialogResult.Cancel();
        _ = _referenceMock.Setup(r => r.Result).Returns(Task.FromResult<DialogResult?>(canceled));
        _ = _launcherMock
            .Setup(l => l.ShowAsync(typeof(RejectRequestDialog), "リクエストを却下", null, It.IsAny<DialogOptions>()))
            .ReturnsAsync(_referenceMock.Object);

        IRenderedComponent<TaggingRequestList> cut = RenderList(CreateProposedRequest());

        cut.WaitForState(() => cut.Markup.Contains("data-testid=\"tagging-request-reject\""));
        cut.Find("[data-testid='tagging-request-reject']").Click();

        _launcherMock.Verify(
            l => l.ShowAsync(
                typeof(RejectRequestDialog),
                "リクエストを却下",
                It.IsAny<DialogParameters?>(),
                It.Is<DialogOptions>(o => o.CloseOnEscapeKey == true && o.MaxWidth == MaxWidth.Small && o.FullWidth == true)),
            Times.Once);
    }

    private static TaggingRequestEntity CreateProposedRequest()
    {
        return new TaggingRequestEntity
        {
            ContractType = "Gratis",
            RequesterUserId = "requester",
            TagOwnerUserId = TagOwnerId,
            TargetItemId = 1,
            RequestedTagId = 2,
            OwnerId = "requester",
            Status = TradeStatus.Proposed,
            RequestType = TaggingRequestType.Add,
            Owner = new ApplicationUser { Id = "requester", UserName = "requester" },
            TargetItem = new SRNSMudApp.Data.Item { Id = 1, Content = "TargetItem", OwnerId = "requester" }
        };
    }

    private IRenderedComponent<TaggingRequestList> RenderList(params TaggingRequestEntity[] requests)
    {
        return _ctx.Render<TaggingRequestList>(parameters => parameters
            .Add(p => p.Requests, requests.ToList())
            .AddCascadingValue(Task.FromResult(BunitTestSetup.CreateAuthState(TagOwnerId))));
    }

    public async ValueTask DisposeAsync() => await _ctx.DisposeAsync();
}