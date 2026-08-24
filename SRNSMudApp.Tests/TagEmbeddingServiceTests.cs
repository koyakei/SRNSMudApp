using System.Numerics.Tensors;

using Microsoft.EntityFrameworkCore;

using SmartComponents.LocalEmbeddings;

using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

namespace SRNSMudApp.Tests;

/// <summary>
///     実際の ITagEmbeddingService（SmartComponents LocalEmbedder）を使い、
///     embedding ベースのコサイン類似度検索コアロジックを検証する。
///     （VectorSearchE2ETests 4ケースの共通核心部分の移行テスト。
///     UI側の描画は TagSearchTests が担当する）
/// </summary>
[Collection(MsSqlCollection.Name)]
public class TagEmbeddingServiceTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private MsSqlTestDatabase _testDb = null!;
    private ApplicationDbContext _db = null!;
    private readonly TagEmbeddingService _embeddingService = new(new LocalEmbedder());

    public TagEmbeddingServiceTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _testDb = await MsSqlTestDatabase.CreateAsync(_fixture.ConnectionString, nameof(TagEmbeddingServiceTests));
        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(_testDb.ConnectionString)
            .Options;
        _db = new ApplicationDbContext(options);

        var user = new ApplicationUser { Id = "embedding-test-user", UserName = "embedding-test-user" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _testDb.DisposeAsync();
    }

    /// <summary>
    ///     「反社会的勢力」で検索すると、意味的に近い「ヤクザ」タグが
    ///     無関係なダミータグ（Target_xxx）よりも高い類似度で返ること。
    /// </summary>
    [Fact]
    public async Task CosineSimilaritySearch_Yakuza_RanksAboveDummyTarget()
    {
        // Arrange: 実サービスで embedding を生成してDB保存
        const string tagName = "ヤクザ";
        var yakuzaEmbedding = (await _embeddingService.GenerateEmbeddingAsync(tagName)).ToArray();
        var dummyTag = new Tag
        {
            Name = $"Target_{Guid.NewGuid():N}",
            OwnerId = "embedding-test-user",
            Embedding = (await _embeddingService.GenerateEmbeddingAsync($"Target_{Guid.NewGuid():N}")).ToArray()
        };
        var yakuzaTag = new Tag { Name = tagName, Content = "", OwnerId = "embedding-test-user", Embedding = yakuzaEmbedding };
        _ = _db.Tags.Add(dummyTag);
        _ = _db.Tags.Add(yakuzaTag);
        _ = await _db.SaveChangesAsync();

        // Act: クエリ文字列の embedding を生成し、コサイン類似度でランク付けする
        var queryEmbedding = (await _embeddingService.GenerateEmbeddingAsync("反社会的勢力")).ToArray();

        System.Collections.Generic.List<(Tag Tag, float Similarity)> ranked = _db.Tags.AsEnumerable()
            .Where(t => t.Embedding != null && t.Embedding.Length == queryEmbedding.Length)
            .Select(t => (t, TensorPrimitives.CosineSimilarity(t.Embedding!, queryEmbedding)))
            .OrderByDescending(x => x.Item2)
            .ToList();

        // Assert: ヤクザが先頭に来ること（Target_xxx より高い類似度）
        Assert.Equal(tagName, ranked[0].Tag.Name);
        var yakuzaSimilarity = ranked.Single(x => x.Tag.Name == tagName).Item2;
        var dummySimilarity = ranked.Single(x => x.Tag.Name.StartsWith("Target_")).Item2;
        Assert.True(yakuzaSimilarity > dummySimilarity,
            $"expected ヤクザ({yakuzaSimilarity:F4}) > dummy({dummySimilarity:F4})");
    }

    /// <summary>
    ///     同一テキストの embedding は同一ベクトルになり、次元は安定していること。
    /// </summary>
    [Fact]
    public async Task GenerateEmbedding_IsDeterministic_AndStableDimension()
    {
        ReadOnlyMemory<float> first = await _embeddingService.GenerateEmbeddingAsync("ヤクザ");
        ReadOnlyMemory<float> second = await _embeddingService.GenerateEmbeddingAsync("ヤクザ");
        ReadOnlyMemory<float> other = await _embeddingService.GenerateEmbeddingAsync("Target_dummy");

        Assert.NotEmpty(first.ToArray());
        Assert.Equal(first.Length, second.Length);
        Assert.Equal(first.Length, other.Length);
        Assert.True(TensorPrimitives.CosineSimilarity(first.Span, second.Span) > 0.999f);
    }
}