using System.Security.Claims;

using Bunit;
using Bunit.TestDoubles;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Contract;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

using ItemEntity = SRNSMudApp.Data.Item;
using TagEntity = SRNSMudApp.Data.Tag;

namespace SRNSMudApp.Tests.Components.Contract;

/// <summary>
///     <see cref="ContractManagement"/> コンポーネントの UI インタラクションテスト。
///     コントラクト受信トレイの空状態表示、および「承認する」「拒否する」ボタン押下時の
///     サービス呼び出しとスナックバー通知を検証する。
/// </summary>
public class ContractManagementTests : IAsyncDisposable
{
    private const string CurrentUserId = "test-contract-user";

    private readonly BunitContext _ctx = new();
    private readonly Mock<IContractManagementDataProvider> _contractDataMock = new();
    private readonly Mock<ITaggingContractService> _contractServiceMock = new();
    private readonly Mock<ISnackbar> _snackbarMock = new();
    private readonly BunitAuthorizationContext _authContext;

    public ContractManagementTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();

        _ctx.Services.RemoveAll<IContractManagementDataProvider>();
        _ctx.Services.AddScoped(_ => _contractDataMock.Object);

        _ctx.Services.RemoveAll<ITaggingContractService>();
        _ctx.Services.AddScoped(_ => _contractServiceMock.Object);

        _ctx.Services.RemoveAll<ISnackbar>();
        _ctx.Services.AddScoped(_ => _snackbarMock.Object);

        _ctx.Services.AddTransient<ContractManagementViewModel>();

        _authContext = _ctx.AddAuthorization();
        _authContext.SetAuthorized("ContractUser");
        _authContext.SetClaims(
            new Claim(ClaimTypes.NameIdentifier, CurrentUserId),
            new Claim(ClaimTypes.Name, "ContractUser"));

        _ = _ctx.Render<MudPopoverProvider>();
    }

    [Fact]
    public void WhenNoIncomingContracts_RendersEmptyAlert()
    {
        // Arrange
        _contractDataMock
            .Setup(d => d.GetContractsAsync(CurrentUserId))
            .ReturnsAsync(new ContractManagementPageData([], []));

        // Act
        IRenderedComponent<ContractManagement> cut = _ctx.Render<ContractManagement>();

        // Assert: 受信トレイの空メッセージが表示されていることを検証
        Assert.Contains("現在、受信したコントラクトの提案はありません。", cut.Markup);
    }

    [Fact]
    public void WhenIncomingContractsExist_ClickingAcceptButton_CallsServiceAndShowsSnackbar()
    {
        // Arrange
        var sampleContract = new TaggingRequestEntity
        {
            Id = 42,
            OwnerId = "sender-user",
            RequestedTag = new TagEntity { Id = 10, Name = "C# 15", OwnerId = "tag-owner" },
            TargetItem = new ItemEntity { Id = 20, Content = "Sample Item Content", OwnerId = CurrentUserId }
        };

        _contractDataMock
            .Setup(d => d.GetContractsAsync(CurrentUserId))
            .ReturnsAsync(new ContractManagementPageData([sampleContract], []));

        _contractServiceMock
            .Setup(s => s.AcceptContractAsync(42, CurrentUserId, null))
            .ReturnsAsync(new Success<string>("Contract accepted"));

        IRenderedComponent<ContractManagement> cut = _ctx.Render<ContractManagement>();

        // Act: 「承認する」ボタンをクリック
        var buttons = cut.FindAll("button");
        var acceptButton = buttons.FirstOrDefault(b => b.TextContent.Contains("承認する"));
        Assert.NotNull(acceptButton);
        acceptButton.Click();

        // Assert: 契約承認処理が呼ばれ、スナックバーが表示されたことを検証
        _contractServiceMock.Verify(
            s => s.AcceptContractAsync(42, CurrentUserId, null),
            Times.Once);

        _snackbarMock.Verify(
            s => s.Add("コントラクトを承認しました。", Severity.Success, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public void WhenIncomingContractsExist_ClickingRejectButton_CallsServiceAndShowsSnackbar()
    {
        // Arrange
        var sampleContract = new TaggingRequestEntity
        {
            Id = 43,
            OwnerId = "sender-user",
            RequestedTag = new TagEntity { Id = 10, Name = "C# 15", OwnerId = "tag-owner" },
            TargetItem = new ItemEntity { Id = 20, Content = "Sample Item Content", OwnerId = CurrentUserId }
        };

        _contractDataMock
            .Setup(d => d.GetContractsAsync(CurrentUserId))
            .ReturnsAsync(new ContractManagementPageData([sampleContract], []));

        _contractServiceMock
            .Setup(s => s.RejectContractAsync(43, CurrentUserId, It.IsAny<string?>()))
            .ReturnsAsync(new Success<string>("Contract rejected"));

        IRenderedComponent<ContractManagement> cut = _ctx.Render<ContractManagement>();

        // Act: 「拒否する」ボタンをクリック
        var buttons = cut.FindAll("button");
        var rejectButton = buttons.FirstOrDefault(b => b.TextContent.Contains("拒否する"));
        Assert.NotNull(rejectButton);
        rejectButton.Click();

        // Assert: 契約拒否処理が呼ばれ、スナックバーが表示されたことを検証
        _contractServiceMock.Verify(
            s => s.RejectContractAsync(43, CurrentUserId, It.IsAny<string?>()),
            Times.Once);

        _snackbarMock.Verify(
            s => s.Add("コントラクトを拒否しました。", Severity.Success, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public void WhenSwitchedToOutboxTab_RendersEmptyOutboxAlert()
    {
        // Arrange: 送信済み・受信済みともに空のコントラクトデータをセットアップ
        _contractDataMock
            .Setup(d => d.GetContractsAsync(CurrentUserId))
            .ReturnsAsync(new ContractManagementPageData([], []));

        // Act: コンポーネントを描画
        IRenderedComponent<ContractManagement> cut = _ctx.Render<ContractManagement>();

        // Assert (初期状態): 初期表示は受信トレイ（Inbox）であり、送信済みの空アラートが表示されていないことを検証
        Assert.Contains("現在、受信したコントラクトの提案はありません。", cut.Markup);
        Assert.DoesNotContain("現在、送信したコントラクトの提案はありません。", cut.Markup);

        // Act: 「送信済み (Outbox)」タブヘッダーをクリックしてタブを切り替え
        var outboxTab = cut.FindAll(".mud-tab").First(t => t.TextContent.Contains("送信済み (Outbox)"));
        outboxTab.Click();

        // Assert (切り替え後): 送信トレイ（Outbox）の空メッセージが表示されていることを検証
        cut.WaitForAssertion(() =>
            Assert.Contains("現在、送信したコントラクトの提案はありません。", cut.Markup));
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}