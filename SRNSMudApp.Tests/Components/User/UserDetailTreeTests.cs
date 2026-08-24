using System.Text.Json;

using AngleSharp.Dom;

using Bunit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using MudBlazor.Services;

using SRNSMudApp.Components.User;
using SRNSMudApp.Data;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

namespace SRNSMudApp.Tests.Components.User;

[Collection(MsSqlCollection.Name)]
public class UserDetailTreeTests : IAsyncLifetime
{
    private const string TreeTabText = "作成したタグツリー";
    private const string TestTagName = "MyUniqueTestTag_12345";

    private readonly MsSqlContainerFixture _fixture;
    private MsSqlTestDatabase _testDb = null!;
    private readonly BunitContext _ctx = new();

    public UserDetailTreeTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _testDb = await MsSqlTestDatabase.CreateAsync(_fixture.ConnectionString, nameof(UserDetailTreeTests));

        _ = _ctx.Services.AddMudServices().AddSrnsComponentServices();

        Bunit.TestDoubles.BunitAuthorizationContext authorization = _ctx.AddAuthorization();
        authorization.SetAuthorized("treetestuser");
        authorization.SetClaims(new System.Security.Claims.Claim(
            System.Security.Claims.ClaimTypes.NameIdentifier, "treetest-user-id"));

        _ctx.Services.AddMsSqlDbFactory(_testDb.ConnectionString);

        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
        await _testDb.DisposeAsync();
    }

    /// <summary>
    ///     ユーザー詳細ページの「作成したタグツリー」タブを開くと、そのユーザーが作成した
    ///     タグが jqTree へ渡されるJSONに含まれることを検証する。
    ///     （UserDetailTreeE2ETests/UserDetail_ShouldShowUserTags_InTree および
    ///     AddTag_FromUI_Then_ViewProfile_ShouldShowTag の移行テスト。両ケースは
    ///     「DB投入済みのタグがツリーへ表示される」点で重複するため統合した）
    /// </summary>
    [Fact]
    public async Task TreeTab_ShowsUserTag_InJqTreeJson()
    {
        // Arrange: ユーザーとタグを事前投入
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        const string testUserId = "treetest-user-id";
        int tagId;
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            _ = dbContext.Users.Add(new ApplicationUser
            {
                Id = testUserId,
                UserName = "treetestuser",
                Email = "treetest@example.com"
            });
            SRNSMudApp.Data.Tag tag = new()
            {
                Name = TestTagName,
                Content = "This is a test tag for tree visualization.",
                OwnerId = testUserId
            };
            _ = dbContext.Tags.Add(tag);
            _ = await dbContext.SaveChangesAsync();
            tagId = tag.Id;
        }

        // Act: タブ「作成したタグツリー」を開き、jqTreeInterop.init に渡されたJSONを取得
        var json = await ActivateTreeTabAndGetJson(testUserId);

        // Assert: タグがJSONに含まれる
        Assert.NotNull(json);
        Assert.Contains($@"""id"":{tagId}", json);
        Assert.Contains($@"""name"":""{TestTagName}""", json);
    }

    /// <summary>
    ///     親タグが他ユーザー所有の場合でも、自分のタグはルートレベルで表示されることを検証する。
    ///     文字列位置ではなくJSONパースによりルート配列直下の存在を確認する。
    ///     （UserDetailTreeE2ETests/UserDetail_ShouldShowUserTags_WhenParentIsOwnedByAnotherUser の移行テスト）
    /// </summary>
    [Fact]
    public async Task TreeTab_ShowsTagAtRootLevel_WhenParentIsOwnedByAnotherUser()
    {
        // Arrange: user_b 所有の親タグを親に持つ user_a のタグを事前投入
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        const string userAId = "user-a-id";
        const string userBId = "user-b-id";
        const string userBTagName = "UserBForeignParentTag";
        const string userATagName = "UserATagWithForeignParent";
        int tagAId;
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            dbContext.Users.AddRange(
                new ApplicationUser { Id = userAId, UserName = "user_a", Email = "usera@example.com" },
                new ApplicationUser { Id = userBId, UserName = "user_b", Email = "userb@example.com" });

            SRNSMudApp.Data.Tag tagB = new() { Name = userBTagName, OwnerId = userBId };
            _ = dbContext.Tags.Add(tagB);
            _ = await dbContext.SaveChangesAsync();

            SRNSMudApp.Data.Tag tagA = new() { Name = userATagName, OwnerId = userAId, ParentTagId = tagB.Id };
            _ = dbContext.Tags.Add(tagA);
            _ = await dbContext.SaveChangesAsync();
            tagAId = tagA.Id;
        }

        // Act: user_a のページでタブ「作成したタグツリー」を開きJSONを取得
        var json = await ActivateTreeTabAndGetJson(userAId);

        // Assert: 自分のタグはルートレベルに存在する（修正前は親が見つからず欠落していた）
        Assert.NotNull(json);
        using var document = JsonDocument.Parse(json!);
        var isRootLevel = document.RootElement.ValueKind == JsonValueKind.Array
                           && document.RootElement.EnumerateArray().Any(node =>
                               node.TryGetProperty("id", out JsonElement id) && id.GetInt32() == tagAId);
        Assert.True(isRootLevel, $"タグ {userATagName}(id={tagAId}) がルートレベルに存在しません。JSON: {json}");

        // 他ユーザーの親タグ自体は表示されない
        Assert.DoesNotContain(userBTagName, json);
    }

    /// <summary>
    ///     UserDetail をレンダリングし、「作成したタグツリー」タブをクリックして
    ///     jqTreeInterop.init に渡されたツリーJSON文字列を返す
    /// </summary>
    private async Task<string?> ActivateTreeTabAndGetJson(string userId)
    {
        var jsInteropInvocations = new List<Bunit.JSRuntimeInvocation>();
        _ = _ctx.JSInterop.SetupVoid("jqTreeInterop.init", invocation =>
        {
            jsInteropInvocations.Add(invocation);
            return true;
        });

        IRenderedComponent<UserDetail> component =
            _ctx.Render<UserDetail>(parameters => parameters.Add(p => p.UserId, userId));

        // ロード完了（ローディング表示の消滅）を待つ
        component.WaitForState(() => !component.Markup.Contains("mud-progress-circular"));

        // 「作成したタグツリー」ラベルをクリック（最も深い要素＝ラベル本体をクリックしバブリングさせる）
        await component.InvokeAsync(() =>
        {
            System.Collections.Generic.IEnumerable<IElement> candidates = component.FindAll("*")
                .Where(e => e.TextContent.Trim() == TreeTabText);
            IElement? tabLabel = candidates.LastOrDefault();
            if (tabLabel == null)
            {
                Assert.Fail($"タブ「{TreeTabText}」が見つかりません。");
            }
            tabLabel!.Click();
        });

        // タブ切替後、OnAfterRenderAsync 経由で jqTreeInterop.init が呼ばれるまで待つ
        component.WaitForAssertion(() => Assert.NotEmpty(jsInteropInvocations));

        Bunit.JSRuntimeInvocation invocation =
            jsInteropInvocations.First(i => i.Identifier == "jqTreeInterop.init");
        return invocation.Arguments[1] as string;
    }
}