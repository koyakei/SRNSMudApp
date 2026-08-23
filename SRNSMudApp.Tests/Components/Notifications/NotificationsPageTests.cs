#region

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using AngleSharp.Dom;

using Bunit;

using SRNSMudApp.Tests.TestSupport;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Pages;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

using Xunit;

#endregion

namespace SRNSMudApp.Tests.Components.Notifications;

public class NotificationsPageTests : IAsyncDisposable
{
    private const string OwnerUserId = "notif-owner-id";
    private const string RequesterUserId = "notif-requester-id";

    private readonly BunitContext _ctx;

    public NotificationsPageTests()
    {
        _ctx = new BunitContext();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddSrnsComponentServices();
        _ctx.Services.AddAuthorizationCore();

        AuthenticationState authState = CreateAuthState(OwnerUserId);
        Mock<AuthenticationStateProvider> authMock = new();
        _ = authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        _ctx.Services.AddScoped(_ => authMock.Object);

        var dbName = Guid.NewGuid().ToString();
        _ = _ctx.Services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

        // ページが依存する実際のサービスとモックを登録
        _ = _ctx.Services.AddScoped<INotificationService, NotificationService>();
        _ctx.Services.AddScoped<TaggingContractService>();
        _ctx.Services.AddScoped(_ => new Mock<ITaggingService>().Object);
        Mock<IItemTagService> itemTagMock = new();
        _ = itemTagMock.Setup(s => s.GetTaggingRequestsForItemAsync(It.IsAny<int>()))
            .ReturnsAsync([]);
        _ = itemTagMock.Setup(s => s.GetItemRepliesAsync(It.IsAny<int>()))
            .ReturnsAsync([]);
        _ctx.Services.AddScoped(_ => itemTagMock.Object);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    /// <summary>
    ///     2件のGratis契約リクエスト通知について、1件目を承認すると「処理済み」チップが表示され
    ///     DB上で契約がExecutedになり、2件目を却下するとダイアログ経由で「却下済み」チップが表示され
    ///     DB上で契約がCanceledになることを検証する。
    ///     （NotificationsTagRequestE2ETests の移行テスト）
    /// </summary>
    [Fact]
    public async Task ApproveAndReject_Requests_UpdateStatusAndDb()
    {
        // Arrange: オーナー宛のリクエスト2件をシード
        (TaggingRequestEntity request1, TaggingRequestEntity request2) = await SeedRequestsAsync();

        RenderFragment page = builder =>
        {
            builder.OpenComponent<NotificationsPage>(0);
            builder.CloseComponent();
        };
        IRenderedComponent<AuthHost> host =
            _ctx.Render<AuthHost>(parameters => parameters.Add(p => p.ChildContent, page));

        host.WaitForState(() => host.Markup.Contains("追加リクエストが届いています"));
        host.WaitForState(() => !host.Markup.Contains("mud-progress-circular"));

        // 2件の通知が表示されていること
        Assert.Contains("タグ追加リクエスト", host.Markup);
        Assert.Equal(2, host.FindAll("button[title='リクエストを承認する']").Count);

        // Act 1: 1件目を承認
        host.FindAll("button[title='リクエストを承認する']").First().Click();

        // Assert 1: 「処理済み」チップが表示され、DBでいずれか1件がExecutedになる
        //           （通知は新しい順に並ぶため、特定行とDOM順の対応は検証しない）
        host.WaitForState(() => host.Markup.Contains("処理済み"));
        Assert.Contains("リクエストを承認しました。", host.Markup);
        await using ApplicationDbContext db1 = CreateDbContext();
        List<TaggingRequestEntity> afterApprove =
            await db1.TaggingRequestEntities!.ToListAsync();
        _ = Assert.Single(afterApprove, r => r.Status == TradeStatus.Executed);

        // Act 2: 2件目を却下（コメント入力ダイアログ）
        host.FindAll("button[title='リクエストを却下する']").First().Click();

        host.WaitForState(() => host.Markup.Contains("mud-dialog"));
        IElement textarea = host.Find("textarea");
        textarea.Input("Rejecting for test");

        host.FindAll("button").First(b => b.TextContent.Contains("却下する")).Click();

        // Assert 2: 「却下済み」チップが表示され、DBでもう1件がCanceledになる
        host.WaitForState(() => host.Markup.Contains("却下済み"));
        Assert.Contains("リクエストを却下しました。", host.Markup);
        await using ApplicationDbContext db2 = CreateDbContext();
        List<TaggingRequestEntity> afterReject =
            await db2.TaggingRequestEntities!.ToListAsync();
        _ = Assert.Single(afterReject, r => r.Status == TradeStatus.Canceled);
        Assert.Equal(2, afterReject.Count(r => r.Status != TradeStatus.Proposed));
    }

    private async Task<(TaggingRequestEntity, TaggingRequestEntity)> SeedRequestsAsync()
    {
        await using ApplicationDbContext db = CreateDbContext();

        _ = db.Users.Add(new ApplicationUser
        {
            Id = OwnerUserId,
            UserName = "notif_owner",
            NormalizedUserName = "NOTIF_OWNER",
            Email = "notif_owner@example.com",
            NormalizedEmail = "NOTIF_OWNER@EXAMPLE.COM"
        });
        _ = db.Users.Add(new ApplicationUser
        {
            Id = RequesterUserId,
            UserName = "notif_requester",
            NormalizedUserName = "NOTIF_REQUESTER",
            Email = "notif_requester@example.com",
            NormalizedEmail = "NOTIF_REQUESTER@EXAMPLE.COM"
        });

        SRNSMudApp.Data.Item targetItem = new()
        {
            Content = "This is a target item for notification test",
            OwnerId = OwnerUserId,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };
        _ = db.Items.Add(targetItem);

        SRNSMudApp.Data.Tag targetTag = new()
        {
            Name = "NotifTag",
            OwnerId = OwnerUserId,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };
        _ = db.Tags.Add(targetTag);
        _ = await db.SaveChangesAsync();

        SRNSMudApp.Data.Item requestItem1 = new() { Content = "Req1", OwnerId = RequesterUserId, CreatedDate = DateTime.UtcNow, UpdatedDate = DateTime.UtcNow };
        SRNSMudApp.Data.Item requestItem2 = new() { Content = "Req2", OwnerId = RequesterUserId, CreatedDate = DateTime.UtcNow, UpdatedDate = DateTime.UtcNow };
        _ = db.Items.Add(requestItem1);
        _ = db.Items.Add(requestItem2);
        _ = await db.SaveChangesAsync();

        var request1 = new TaggingRequestEntity
        {
            ContractType = "Gratis",
            OwnerId = RequesterUserId,
            RequesterUserId = RequesterUserId,
            TagOwnerUserId = OwnerUserId,
            TargetItemId = targetItem.Id,
            RequestedTagId = targetTag.Id,
            RequestItemId = requestItem1.Id,
            RequestType = TaggingRequestType.Add,
            ProposedWeight = 1,
            Status = TradeStatus.Proposed,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };
        var request2 = new TaggingRequestEntity
        {
            ContractType = "Gratis",
            OwnerId = RequesterUserId,
            RequesterUserId = RequesterUserId,
            TagOwnerUserId = OwnerUserId,
            TargetItemId = targetItem.Id,
            RequestedTagId = targetTag.Id,
            RequestItemId = requestItem2.Id,
            RequestType = TaggingRequestType.Add,
            ProposedWeight = 1,
            Status = TradeStatus.Proposed,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        _ = db.TaggingRequestEntities.Add(request1);
        _ = db.TaggingRequestEntities.Add(request2);
        _ = await db.SaveChangesAsync();

        requestItem1.TaggingRequestEntityId = request1.Id;
        requestItem2.TaggingRequestEntityId = request2.Id;
        _ = await db.SaveChangesAsync();

        return (request1, request2);
    }

    private ApplicationDbContext CreateDbContext()
    {
        return _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();
    }

    private static AuthenticationState CreateAuthState(string userId)
    {
        Claim[] claims = [new(ClaimTypes.NameIdentifier, userId), new(ClaimTypes.Name, userId)];
        ClaimsIdentity identity = new(claims, "TestAuthType");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    /// <summary>
    ///     認証カスケード・スナックバー・ダイアログプロバイダを提供するホスト。
    /// </summary>
    private sealed class AuthHost : ComponentBase
    {
        [Parameter] public RenderFragment ChildContent { get; set; } = _ => { };

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingAuthenticationState>(0);
            builder.AddAttribute(1, nameof(CascadingAuthenticationState.ChildContent), (RenderFragment)(b =>
            {
                b.OpenComponent<MudSnackbarProvider>(0);
                b.CloseComponent();
                b.OpenComponent<MudDialogProvider>(1);
                b.CloseComponent();
                b.AddContent(2, ChildContent);
            }));
            builder.CloseComponent();
        }
    }
}
