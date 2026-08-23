#region

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using Bunit;

using SRNSMudApp.Tests.TestSupport;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Components.Item;
using SRNSMudApp.Data;
using Xunit;

#endregion

namespace SRNSMudApp.Tests.Components.Item;

public class AddItemTests : IAsyncDisposable
{
    private const string ExistingUserId = "test-user-id";
    private const string TestContent = "Test Item Content";

    private readonly BunitContext _ctx;

    public AddItemTests()
    {
        _ctx = new BunitContext();

        _ = _ctx.Services.AddMudServices().AddSrnsComponentServices();

        // AddItem は [CascadingParameter] Task<AuthenticationState> で認証情報を受ける
        Claim[] claims = [new(ClaimTypes.NameIdentifier, ExistingUserId), new(ClaimTypes.Name, "testuser")];
        var identity = new ClaimsIdentity(claims, "TestAuthType");
        var authState = new AuthenticationState(new ClaimsPrincipal(identity));
        _ = _ctx.Services.AddCascadingValue(_ => Task.FromResult(authState));
        _ = _ctx.Services.AddAuthorizationCore();

        // UserManager は DI 解決にのみ必要（保存処理では未使用）のためモックで差し込む
        var storeMock = new Mock<IUserStore<ApplicationUser>>();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(storeMock.Object, null!, null!, null!, null!,
            null!, null!, null!, null!);
        _ = _ctx.Services.AddScoped(_ => userManagerMock.Object);

        var dbName = Guid.NewGuid().ToString();
        _ = _ctx.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped, ServiceLifetime.Singleton);
        _ = _ctx.Services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    /// <summary>
    ///     既存の ApplicationUser を OwnerId に持つ Item を保存しても、
    ///     Owner が新規エンティティとして追加され主キー重複例外が起きないことを検証する。
    ///     （ItemAddE2ETests/AddItem_FailsIfOwnerIsAddedAsNewEntity の移行テスト）
    /// </summary>
    [Fact]
    public async Task Save_WithExistingOwner_DoesNotDuplicateUserAndStoresItem()
    {
        // Arrange: 既存ユーザーを事前にDBへ保存しておく（「既存ユーザーであること」が再現条件）
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            _ = dbContext.Users.Add(new ApplicationUser
            {
                Id = ExistingUserId, UserName = "testuser", Email = "test@example.com"
            });
            _ = await dbContext.SaveChangesAsync();
        }

        IRenderedComponent<AddItem> cut = _ctx.Render<AddItem>();

        // OnInitializedAsync で認証情報から _newItem が生成されフォームが描画されるまで待つ
        cut.WaitForState(() => cut.FindAll("form").Count > 0);

        // textarea（Lines=3）にコンテンツを入力
        cut.Find("textarea").Input(TestContent);

        // Act: フォーム送信 → HandleValidSubmit 実行（例外は bUnit が握り潰さず失敗させるため、
        // 主キー重複が起きた場合はこのテスト自体が落ちるか、下記のDB検証で落ちる）
        cut.Find("form").Submit();

        // Assert: アイテムが1件保存され、OwnerId が既存ユーザーと一致する
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            SRNSMudApp.Data.Item? saved =
                await dbContext.Items.FirstOrDefaultAsync(i => i.Content == TestContent);
            Assert.NotNull(saved);
            Assert.Equal(ExistingUserId, saved!.OwnerId);

            // Owner が新規エンティティとして追加されていないこと（ユーザーは1件のまま）
            Assert.Equal(1, await dbContext.Users.CountAsync(u => u.Id == ExistingUserId));
        }
    }
}
