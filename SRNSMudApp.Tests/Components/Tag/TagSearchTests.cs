#region

using System;
using System.Threading;
using System.Threading.Tasks;

using AngleSharp.Dom;

using Bunit;

using SRNSMudApp.Tests.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

using Xunit;

#endregion

namespace SRNSMudApp.Tests.Components.Tag;

/// <summary>
///     タグ検索ページの「入力→embedding類似度検索→候補表示」を検証する。
///     embedding 生成品質は TagEmbeddingServiceTests（実サービス）が担保するため、
///     本テストは固定ベクトルを返すフェイクで配線を検証する。
///     （VectorSearchE2ETests.VectorSearch_TagSearchPage の移行テスト。
///     ItemList/ImportTag/TagAddDialog は同一の検索ロジックを呼ぶため重複検証はしない）
/// </summary>
public class TagSearchTests : IAsyncDisposable
{
    /// <summary>
    ///     類義語グループごとに同じベクトルを返すフェイク embedding サービス用の辞書。
    /// </summary>
    private static readonly string[][] SynonymGroups =
    [
        ["ヤクザ", "反社会的勢力", "暴力団"],
        ["猫", "ねこ", "ネコ"]
    ];

    private readonly BunitContext _ctx;

    public TagSearchTests()
    {
        _ctx = new BunitContext();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddSrnsComponentServices();

        _ctx.Services.AddScoped<ITagEmbeddingService>(_ => new FakeEmbeddingService());

        var dbName = Guid.NewGuid().ToString();
        _ = _ctx.Services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    /// <summary>
    ///     「反社会的勢力」と入力すると、意味的に近い「ヤクザ」タグが候補に表示されること。
    /// </summary>
    [Fact]
    public async Task TypingSemanticallySimilarQuery_ShowsYakuzaTagInCandidates()
    {
        // Arrange: ヤクザ（類似ベクトル）と無関係タグを投入
        await using (ApplicationDbContext dbContext = CreateDbContext())
        {
            _ = dbContext.Tags.Add(new SRNSMudApp.Data.Tag
            {
                Name = "ヤクザ",
                Content = "",
                OwnerId = "vector-user",
                Embedding = [1f, 0f, 0f, 0f]
            });
            _ = dbContext.Tags.Add(new SRNSMudApp.Data.Tag
            {
                Name = $"Target_{Guid.NewGuid():N}",
                Content = "",
                OwnerId = "vector-user",
                Embedding = [0f, 1f, 0f, 0f]
            });
            _ = await dbContext.SaveChangesAsync();
        }

        IRenderedComponent<MudBlazor.MudPopoverProvider> provider = _ctx.Render<MudBlazor.MudPopoverProvider>();
        IRenderedComponent<TagSearch> cut = _ctx.Render<TagSearch>();

        // Act: 検索入力
        IElement input = cut.Find("input");
        input.Input("反社会的勢力");

        // Assert: 候補にヤクザが表示される
        provider.WaitForState(() => provider.Markup.Contains("ヤクザ"), TimeSpan.FromSeconds(5));

        Assert.Contains("ヤクザ", provider.Markup);
    }

    private ApplicationDbContext CreateDbContext()
    {
        return _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();
    }

    /// <summary>
    ///     類義語は同じベクトル、それ以外は直交ベクトルを返すフェイク。
    /// </summary>
    private sealed class FakeEmbeddingService : ITagEmbeddingService
    {
        public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text)
        {
            float[] vector = SynonymGroups.Any(g => g.Any(s => text.Contains(s)))
                ? [1f, 0f, 0f, 0f]
                : [0f, 1f, 0f, 0f];
            return Task.FromResult(new ReadOnlyMemory<float>(vector));
        }
    }
}
