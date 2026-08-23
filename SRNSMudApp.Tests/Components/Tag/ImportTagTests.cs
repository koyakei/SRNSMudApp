#region

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Bunit;

using SRNSMudApp.Tests.TestSupport;
using Moq;
using Bunit.TestDoubles;

using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using Xunit;

#endregion

namespace SRNSMudApp.Tests.Components.Tag;

// BunitContextの継承をやめ、IAsyncDisposableを実装します
public class ImportTagTests : IAsyncDisposable
{
    private readonly BunitContext _ctx;

    public ImportTagTests()
    {
        _ctx = new BunitContext();

        // 認証モック・MudServices・アプリ側サービスは BunitTestSetup に集約
        _ = _ctx.Services.AddAuth("testuser");
        _ = _ctx.Services.AddSrnsComponentServices();
        // ImportTag は [CascadingParameter] Task<AuthenticationState> で認証情報を受けるためカスケード値も登録する
        _ = _ctx.Services.AddCascadingValue(_ => System.Threading.Tasks.Task.FromResult(BunitTestSetup.CreateAuthState("testuser")));

        var dbName = Guid.NewGuid().ToString();
        // ImportTag.ImportData が BeginTransactionAsync を呼ぶため InMemory のトランザクション警告を無視する
        _ = _ctx.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)),
            ServiceLifetime.Scoped, ServiceLifetime.Singleton);
        _ = _ctx.Services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

        var tagEmbeddingServiceMock = new Mock<ITagEmbeddingService>();
        _ = _ctx.Services.AddScoped(sp => tagEmbeddingServiceMock.Object);

        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // 非同期でBunitContextを破棄し、MudBlazorの非同期サービスの例外を防ぐ
    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
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
}