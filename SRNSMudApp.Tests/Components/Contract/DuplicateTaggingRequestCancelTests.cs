#region

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using AngleSharp.Dom;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Components.Contract;
using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

#endregion

namespace SRNSMudApp.Tests.Components.Contract;

/// <summary>
///     同一アイテム・同一タグへの重複タグ付けリクエストで、タグオーナーが片方（A）を承認しても
///     もう片方（B）のリクエストには影響せず、B は自分の契約を送信済みタブから取り下げできることを検証する。
///     （DuplicateTaggingRequestCancelE2ETests の移行テスト。マルチログイン部分は
///     「承認側」と「取り下げ側」の2つのコンポーネントテストに分解している）
/// </summary>
public class DuplicateTaggingRequestCancelTests : IAsyncDisposable
{
    private const string TagOwnerId = "dup-tag-owner";
    private const string UserAId = "dup-user-a";
    private const string UserBId = "dup-user-b";

    private readonly BunitContext _ctx;
    private readonly Mock<IContractDataProvider> _contractDataMock = new();

    public DuplicateTaggingRequestCancelTests()
    {
        _ctx = new BunitContext();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddSrnsComponentServices();
        _ctx.Services.AddAuthorizationCore();

        var dbName = Guid.NewGuid().ToString();
        _ = _ctx.Services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

        AuthenticationState authState = BunitTestSetup.CreateAuthState(UserBId);
        Mock<AuthenticationStateProvider> authMock = new();
        _ = authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        _ctx.Services.AddScoped(_ => authMock.Object);

        _ctx.Services.AddScoped<IItemTagService, ItemTagService>();
        _ctx.Services.AddScoped<ITaggingService, TaggingService>();
        _ctx.Services.AddScoped<TaggingContractService>();

        // ContractManagement 用にデータプロバイダをモックへ差し替え
        _ctx.Services.RemoveAll<IContractDataProvider>();
        _ctx.Services.AddSingleton(_ => _contractDataMock.Object);
    }

    public async ValueTask DisposeAsync() => await _ctx.DisposeAsync();

    /// <summary>
    ///     タグオーナーがユーザーAの重複リクエストを承認しても、ユーザーBの
    ///     リクエストは Proposed のまま影響を受けないこと。
    /// </summary>
    [Fact]
    public async Task ApprovingRequestFromUserA_DoesNotAffectDuplicateRequestFromUserB()
    {
        (TaggingRequestEntity contractA, TaggingRequestEntity contractB) =
            await SeedContractsAndRequestsAsync();

        // Act: タグオーナーとしてリクエスト一覧を表示し、ユーザーAの行のみ承認する
        IRenderedComponent<TaggingRequestList> cut = _ctx.Render<TaggingRequestList>(parameters => parameters
            .Add(p => p.Requests, new[] { contractA, contractB })
            .AddCascadingValue(Task.FromResult(BunitTestSetup.CreateAuthState(TagOwnerId))));

        cut.WaitForState(() => cut.Markup.Contains("data-testid=\"tagging-request-approve\""));

        IElement rowA = cut.FindAll("tr").First(tr => tr.TextContent.Contains(UserAId));
        rowA.QuerySelector("[data-testid='tagging-request-approve']")!.Click();

        // Assert: A は Executed、B は Proposed のまま (実アプリでは OnRequestChanged で再読込される)
        await using ApplicationDbContext db =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();
        Assert.Equal(TradeStatus.Executed, db.TaggingRequestEntities.Find(contractA.Id)!.Status);
        Assert.Equal(TradeStatus.Proposed, db.TaggingRequestEntities.Find(contractB.Id)!.Status);
    }

    /// <summary>
    ///     ユーザーBが送信済みタブから自分の提案中コントラクトを取り下げると、
    ///     スナックバーが表示され DB 上で Canceled になること。
    /// </summary>
    [Fact]
    public async Task OutboxWithdrawButton_CancelsOwnProposedContract()
    {
        (_, TaggingRequestEntity contractB) = await SeedContractsAndRequestsAsync();

        // Arrange: 送信済みタブに自分の提案中コントラクトを返すようモック
        List<TaggingRequestEntity> outbox;
        await using (ApplicationDbContext seedDb =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext())
        {
            outbox = await seedDb.TaggingRequestEntities!
                .Include(r => r.RequestedTag)
                .Include(r => r.TargetItem)
                .Where(r => r.Id == contractB.Id)
                .ToListAsync();
        }

        _ = _contractDataMock.Setup(d => d.GetContractsAsync(UserBId))
            .ReturnsAsync(new ContractManagementPageData([], outbox));

        RenderFragment page = builder =>
        {
            builder.OpenComponent<ContractManagement>(0);
            builder.CloseComponent();
        };
        IRenderedComponent<SnackbarHost> host =
            _ctx.Render<SnackbarHost>(parameters => parameters.Add(p => p.ChildContent, page));

        // Act: 送信済みタブを開き、取り下げる
        host.WaitForState(() => host.Markup.Contains("送信済み"));
        host.FindAll(".mud-tab").First(t => t.TextContent.Contains("送信済み")).Click();

        host.WaitForState(() => host.Markup.Contains("提案中"));
        host.FindAll("button").First(b => b.TextContent.Contains("取り下げる")).Click();

        // Assert: 取り下げ完了スナックバーと DB ステータス
        host.WaitForState(() => host.Markup.Contains("コントラクトを取り下げました。"),
            TimeSpan.FromSeconds(10));

        await using ApplicationDbContext db =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();
        Assert.Equal(TradeStatus.Canceled, db.TaggingRequestEntities.Find(contractB.Id)!.Status);
    }

    private async Task<(TaggingRequestEntity A, TaggingRequestEntity B)> SeedContractsAndRequestsAsync()
    {
        await using ApplicationDbContext db =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();

        db.Users.AddRange(
            new ApplicationUser { Id = TagOwnerId, UserName = TagOwnerId },
            new ApplicationUser { Id = UserAId, UserName = UserAId },
            new ApplicationUser { Id = UserBId, UserName = UserBId });

        SRNSMudApp.Data.Tag tag = new() { Name = $"DupTag_{Guid.NewGuid():N}", OwnerId = TagOwnerId };
        SRNSMudApp.Data.Item item = new() { Content = $"Dup item {Guid.NewGuid():N}", OwnerId = UserAId };
        db.Tags.Add(tag);
        db.Items.Add(item);
        _ = await db.SaveChangesAsync();

        TaggingRequestEntity Create(string ownerId) => new()
        {
            ContractType = "Gratis",
            OwnerId = ownerId,
            RequesterUserId = ownerId,
            TagOwnerUserId = TagOwnerId,
            TargetItemId = item.Id,
            RequestedTagId = tag.Id,
            Status = TradeStatus.Proposed,
            Payload = new GratisPayload("dup scenario"),
            RequestType = TaggingRequestType.Add
        };

        TaggingRequestEntity contractA = Create(UserAId);
        TaggingRequestEntity contractB = Create(UserBId);
        contractA.Owner = db.Users.Find(UserAId)!;
        contractB.Owner = db.Users.Find(UserBId)!;
        db.TaggingRequestEntities!.AddRange(contractA, contractB);
        _ = await db.SaveChangesAsync();

        return (contractA, contractB);
    }

    /// <summary>認証カスケード + スナックバー + ダイアログを提供するホスト。</summary>
    private sealed class SnackbarHost : ComponentBase
    {
        [Parameter] public RenderFragment ChildContent { get; set; } = _ => { };

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingAuthenticationState>(0);
            builder.AddAttribute(1, nameof(CascadingAuthenticationState.ChildContent), (RenderFragment)(b =>
            {
                b.OpenComponent<MudBlazor.MudSnackbarProvider>(0);
                b.CloseComponent();
                b.OpenComponent<MudBlazor.MudDialogProvider>(1);
                b.CloseComponent();
                b.AddContent(2, ChildContent);
            }));
            builder.CloseComponent();
        }
    }
}