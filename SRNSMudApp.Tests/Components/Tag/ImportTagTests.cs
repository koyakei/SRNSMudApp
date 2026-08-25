using System.Text;

using AngleSharp.Dom;

using Bunit;
using Bunit.TestDoubles;

using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

namespace SRNSMudApp.Tests.Components.Tag;

[Collection(MsSqlCollection.Name)]
public class ImportTagTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private MsSqlTestDatabase _testDb = null!;
    private readonly BunitContext _ctx = new();

    public ImportTagTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _testDb = await MsSqlTestDatabase.CreateAsync(_fixture.ConnectionString, nameof(ImportTagTests));

        _ = _ctx.Services.AddAuth("testuser");
        _ = _ctx.Services.AddSrnsComponentServices();
        _ = _ctx.Services.AddCascadingValue(_ => System.Threading.Tasks.Task.FromResult(BunitTestSetup.CreateAuthState("testuser")));

        _ctx.Services.AddMsSqlDbFactory(_testDb.ConnectionString);

        var tagEmbeddingServiceMock = new Mock<ITagEmbeddingService>();
        _ = _ctx.Services.AddScoped(sp => tagEmbeddingServiceMock.Object);

        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        await using var dbContext = new ApplicationDbContext(_testDb.Options);
        dbContext.Users.AddRange(
            new ApplicationUser { Id = "testuser", UserName = "testuser" },
            new ApplicationUser { Id = "system", UserName = "system" },
            new ApplicationUser { Id = "otheruser", UserName = "otheruser" }
        );
        await dbContext.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
        await _testDb.DisposeAsync();
    }

    [Fact]
    public async Task ImportButton_ShouldBeDisabled_WhenParentTagIsNotSelected_AndEnabled_WhenSelected()
    {
        // Arrange
        // Render ではなく _ctx.RenderComponent を使用します
        IRenderedComponent<ImportTag> component = _ctx.Render<ImportTag>();

        // Simulate file selection so the import button is rendered
        var fileMock = new Mock<IBrowserFile>();
        _ = fileMock.Setup(f => f.Name).Returns("test.csv");
        _ = fileMock.Setup(f => f.Size).Returns(100);

        IRenderedComponent<MudFileUpload<IBrowserFile>> fileUpload =
            component.FindComponent<MudFileUpload<IBrowserFile>>();
        await component.InvokeAsync(() => fileUpload.Instance.FilesChanged.InvokeAsync(fileMock.Object));

        component.Render();

        // Find the import button (the only Success colored button in this component)
        IReadOnlyList<IRenderedComponent<MudButton>> buttons = component.FindComponents<MudButton>();
        IRenderedComponent<MudButton>? importButton = buttons.FirstOrDefault(b => b.Instance.Color == Color.Success);

        Assert.NotNull(importButton);

        // Assert: Parent tag is initially not selected, button should be disabled
        Assert.True(importButton.Instance.Disabled);

        // Act: Select a parent tag
        IRenderedComponent<MudAutocomplete<SRNSMudApp.Data.Tag>> autocomplete =
            component.FindComponent<MudAutocomplete<SRNSMudApp.Data.Tag>>();
        var parentTag = new SRNSMudApp.Data.Tag { Id = 1, Name = "Parent Tag", OwnerId = "test" };
        await component.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(parentTag));

        component.Render();

        // Assert: Parent tag is now selected, button should be enabled
        Assert.False(importButton.Instance.Disabled);
    }

    /// <summary>
    ///     選択した親タグの下にCSV（"Animal,Dog\nAnimal,Cat"）をインポートすると、
    ///     Animal が親タグの子として作られ、Dog/Cat が Animal の子として作られることを検証する。
    ///     （ImportTagE2ETests/ImportTag_WithParentTag_ShouldImportTagsUnderParent の移行テスト）
    /// </summary>
    [Fact]
    public async Task ImportCsv_UnderSelectedParent_CreatesTwoLevelHierarchy()
    {
        // Arrange: 既存ユーザー所有の親タグを1件事前登録
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        SRNSMudApp.Data.Tag rootTag;
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            rootTag = new SRNSMudApp.Data.Tag { Name = "RootTag", OwnerId = "testuser" };
            _ = dbContext.Tags.Add(rootTag);
            _ = await dbContext.SaveChangesAsync();
        }

        // CSVファイルの実体は OpenReadStream 経由で読まれるためモックで返す
        const string csvContent = "Animal,Dog\nAnimal,Cat";
        var fileMock = new Mock<IBrowserFile>();
        _ = fileMock.Setup(f => f.Name).Returns("tags.csv");
        _ = fileMock.Setup(f => f.Size).Returns(Encoding.UTF8.GetByteCount(csvContent));
        _ = fileMock.Setup(f => f.OpenReadStream(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(new MemoryStream(Encoding.UTF8.GetBytes(csvContent)));

        IRenderedComponent<ImportTag> component = _ctx.Render<ImportTag>();

        // 親タグ選択: オートコンプリート操作の代わりに ValueChanged を直接発火して _selectedParentTag を設定
        IRenderedComponent<MudAutocomplete<SRNSMudApp.Data.Tag>> autocomplete =
            component.FindComponent<MudAutocomplete<SRNSMudApp.Data.Tag>>();
        await component.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(rootTag));

        // ファイル選択: InputFile 操作の代わりに MudFileUpload の FilesChanged を直接発火して _file を設定
        IRenderedComponent<MudFileUpload<IBrowserFile>> fileUpload =
            component.FindComponent<MudFileUpload<IBrowserFile>>();
        await component.InvokeAsync(() => fileUpload.Instance.FilesChanged.InvokeAsync(fileMock.Object));

        component.Render();

        // Act: インポート実行ボタン（Color.Success）をクリック
        IReadOnlyList<IRenderedComponent<MudButton>> buttons = component.FindComponents<MudButton>();
        IRenderedComponent<MudButton>? importButton = buttons.FirstOrDefault(b => b.Instance.Color == Color.Success);
        Assert.NotNull(importButton);
        importButton.Find("button").Click();

        // 非同期のインポート処理完了待ち（Cat まで作成されたことをポーリング）
        try
        {
            component.WaitForState(() =>
            {
                using ApplicationDbContext ctx = dbFactory.CreateDbContext();
                return ctx.Tags.Any(t => t.Name == "Cat");
            });
        }
        catch (Bunit.Extensions.WaitForHelpers.WaitForFailedException)
        {
            System.IO.File.WriteAllText("/tmp/opencode/import_markup.html", component.Markup);
            throw;
        }

        // Assert: Root→Animal→Dog/Cat の2階層の親子関係がDB上に成立している
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            List<SRNSMudApp.Data.Tag> tags =
                await dbContext.Tags.Where(t => t.OwnerId == "testuser").ToListAsync();

            SRNSMudApp.Data.Tag animal = tags.Single(t => t.Name == "Animal");
            Assert.Equal(rootTag.Id, animal.ParentTagId);

            SRNSMudApp.Data.Tag dog = tags.Single(t => t.Name == "Dog");
            Assert.Equal(animal.Id, dog.ParentTagId);

            SRNSMudApp.Data.Tag cat = tags.Single(t => t.Name == "Cat");
            Assert.Equal(animal.Id, cat.ParentTagId);
        }
    }

    [Fact]
    public async Task SearchUserTagsAsync_ShouldIncludeSystemTags()
    {
        // Arrange
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        IImportTagDataProvider provider = _ctx.Services.GetRequiredService<IImportTagDataProvider>();

        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            dbContext.Tags.AddRange(
                new SRNSMudApp.Data.Tag { Name = "UserTag1", OwnerId = "testuser" },
                new SRNSMudApp.Data.Tag { Name = "SystemRootTag", IsSystem = true, OwnerId = "system" },
                new SRNSMudApp.Data.Tag { Name = "OtherUserTag", OwnerId = "otheruser" }
            );
            await dbContext.SaveChangesAsync();
        }

        // Act
        IReadOnlyList<SRNSMudApp.Data.Tag> results = await provider.SearchUserTagsAsync("testuser", "");

        // Assert: ユーザー所有タグとシステムタグが含まれ、別ユーザーの非システムタグは含まれない
        Assert.Contains(results, t => t.Name == "UserTag1");
        Assert.Contains(results, t => t.Name == "SystemRootTag");
        Assert.DoesNotContain(results, t => t.Name == "OtherUserTag");
    }

    [Fact]
    public async Task ImportCsv_UnderSelectedSystemParent_CreatesHierarchyUnderSystemParent()
    {
        // Arrange: システムタグを親タグとして事前登録
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        SRNSMudApp.Data.Tag systemRootTag;
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            systemRootTag = new SRNSMudApp.Data.Tag { Name = "SystemCategory", IsSystem = true, OwnerId = "system" };
            _ = dbContext.Tags.Add(systemRootTag);
            _ = await dbContext.SaveChangesAsync();
        }

        const string csvContent = "Science,Physics";
        var fileMock = new Mock<IBrowserFile>();
        _ = fileMock.Setup(f => f.Name).Returns("tags.csv");
        _ = fileMock.Setup(f => f.Size).Returns(Encoding.UTF8.GetByteCount(csvContent));
        _ = fileMock.Setup(f => f.OpenReadStream(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(new MemoryStream(Encoding.UTF8.GetBytes(csvContent)));

        IRenderedComponent<ImportTag> component = _ctx.Render<ImportTag>();

        // 親タグとしてシステムタグを選択
        IRenderedComponent<MudAutocomplete<SRNSMudApp.Data.Tag>> autocomplete =
            component.FindComponent<MudAutocomplete<SRNSMudApp.Data.Tag>>();
        await component.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(systemRootTag));

        // ファイル選択
        IRenderedComponent<MudFileUpload<IBrowserFile>> fileUpload =
            component.FindComponent<MudFileUpload<IBrowserFile>>();
        await component.InvokeAsync(() => fileUpload.Instance.FilesChanged.InvokeAsync(fileMock.Object));

        component.Render();

        // Act: インポート実行
        IReadOnlyList<IRenderedComponent<MudButton>> buttons = component.FindComponents<MudButton>();
        IRenderedComponent<MudButton>? importButton = buttons.FirstOrDefault(b => b.Instance.Color == Color.Success);
        Assert.NotNull(importButton);
        importButton.Find("button").Click();

        // 非同期のインポート処理完了待ち
        component.WaitForState(() =>
        {
            using ApplicationDbContext ctx = dbFactory.CreateDbContext();
            return ctx.Tags.Any(t => t.Name == "Physics");
        });

        // Assert: SystemCategory → Science → Physics の親子関係が成立している
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            List<SRNSMudApp.Data.Tag> userTags =
                await dbContext.Tags.Where(t => t.OwnerId == "testuser").ToListAsync();

            SRNSMudApp.Data.Tag science = userTags.Single(t => t.Name == "Science");
            Assert.Equal(systemRootTag.Id, science.ParentTagId);

            SRNSMudApp.Data.Tag physics = userTags.Single(t => t.Name == "Physics");
            Assert.Equal(science.Id, physics.ParentTagId);
        }
    }

    [Fact]
    public void NonAdminUser_DoesNotShowSystemTagImportSwitch()
    {
        // Arrange & Act (デフォルトは "testuser" / 非 Admin)
        IRenderedComponent<ImportTag> component = _ctx.Render<ImportTag>();

        // Assert: システムタグインポート用スイッチが表示されない
        IReadOnlyList<IRenderedComponent<MudSwitch<bool>>> switches =
            component.FindComponents<MudSwitch<bool>>();
        Assert.Empty(switches);
    }

    [Fact]
    public async Task AdminUser_ShowsSystemTagImportSwitch_AndImportsAsSystemWhenEnabled()
    {
        // Arrange: Admin コンテキストを作成
        await using var adminCtx = new BunitContext();
        _ = adminCtx.Services.AddAuth("adminuser", "Admin");
        _ = adminCtx.Services.AddSrnsComponentServices();
        _ = adminCtx.Services.AddCascadingValue(_ =>
            Task.FromResult(BunitTestSetup.CreateAuthState("adminuser", "Admin")));
        adminCtx.Services.AddMsSqlDbFactory(_testDb.ConnectionString);

        var tagEmbeddingServiceMock = new Mock<ITagEmbeddingService>();
        _ = adminCtx.Services.AddScoped(sp => tagEmbeddingServiceMock.Object);
        adminCtx.JSInterop.Mode = JSRuntimeMode.Loose;

        IDbContextFactory<ApplicationDbContext> dbFactory =
            adminCtx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();

        SRNSMudApp.Data.Tag rootTag;
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            dbContext.Users.Add(new ApplicationUser { Id = "adminuser", UserName = "adminuser" });
            rootTag = new SRNSMudApp.Data.Tag { Name = "AdminRoot", OwnerId = "adminuser" };
            _ = dbContext.Tags.Add(rootTag);
            _ = await dbContext.SaveChangesAsync();
        }

        const string csvContent = "SystemDomain,SystemService";
        var fileMock = new Mock<IBrowserFile>();
        _ = fileMock.Setup(f => f.Name).Returns("tags.csv");
        _ = fileMock.Setup(f => f.Size).Returns(Encoding.UTF8.GetByteCount(csvContent));
        _ = fileMock.Setup(f => f.OpenReadStream(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(new MemoryStream(Encoding.UTF8.GetBytes(csvContent)));

        IRenderedComponent<ImportTag> component = adminCtx.Render<ImportTag>();

        // Assert: スイッチが表示されている
        IRenderedComponent<MudSwitch<bool>> switchComp =
            component.FindComponent<MudSwitch<bool>>();
        Assert.NotNull(switchComp);

        // Act: スイッチを ON にする
        await component.InvokeAsync(() => switchComp.Instance.ValueChanged.InvokeAsync(true));

        // 親タグ選択
        IRenderedComponent<MudAutocomplete<SRNSMudApp.Data.Tag>> autocomplete =
            component.FindComponent<MudAutocomplete<SRNSMudApp.Data.Tag>>();
        await component.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(rootTag));

        // ファイル選択
        IRenderedComponent<MudFileUpload<IBrowserFile>> fileUpload =
            component.FindComponent<MudFileUpload<IBrowserFile>>();
        await component.InvokeAsync(() => fileUpload.Instance.FilesChanged.InvokeAsync(fileMock.Object));

        component.Render();

        // インポート実行
        IReadOnlyList<IRenderedComponent<MudButton>> buttons = component.FindComponents<MudButton>();
        IRenderedComponent<MudButton>? importButton = buttons.FirstOrDefault(b => b.Instance.Color == Color.Success);
        Assert.NotNull(importButton);
        importButton.Find("button").Click();

        // 完了待ち
        component.WaitForState(() =>
        {
            using ApplicationDbContext ctx = dbFactory.CreateDbContext();
            return ctx.Tags.Any(t => t.Name == "SystemService");
        });

        // Assert: 作成されたタグの OwnerId が "system" かつ IsSystem が true であること
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            List<SRNSMudApp.Data.Tag> systemTags =
                await dbContext.Tags.Where(t => t.OwnerId == "system").ToListAsync();

            SRNSMudApp.Data.Tag domain = systemTags.Single(t => t.Name == "SystemDomain");
            Assert.True(domain.IsSystem);
            Assert.Equal("system", domain.OwnerId);
            Assert.Equal(rootTag.Id, domain.ParentTagId);

            SRNSMudApp.Data.Tag service = systemTags.Single(t => t.Name == "SystemService");
            Assert.True(service.IsSystem);
            Assert.Equal("system", service.OwnerId);
            Assert.Equal(domain.Id, service.ParentTagId);
        }
    }
}