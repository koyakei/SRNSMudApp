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
/// </summary>
public class TagEmbeddingServiceTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;
    private readonly TagEmbeddingService _embeddingService = new(new LocalEmbedder());

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    ///     「反社会的勢力」で検索すると、意味的に近い「ヤクザ」タグが
    ///     無関係なダミータグ（Target_xxx）よりも高い類似度で返ること。
    /// </summary>
    [Fact]
    public async Task CosineSimilaritySearch_Yakuza_RanksAboveDummyTarget()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var userId = $"emb_{tid}";
        await using var db = new ApplicationDbContext(_sharedDb.Options);

        await db.SeedUsersAsync(userId);

        // Arrange: 実サービスで embedding を生成してDB保存
        var tagName = $"ヤクザ_{tid}";
        var yakuzaEmbedding = (await _embeddingService.GenerateEmbeddingAsync("ヤクザ")).ToArray();
        var dummyTag = new Tag
        {
            Name = $"Target_{tid}",
            OwnerId = userId,
            Embedding = (await _embeddingService.GenerateEmbeddingAsync($"Target_{Guid.NewGuid():N}")).ToArray()
        };
        var yakuzaTag = new Tag { Name = tagName, Content = "", OwnerId = userId, Embedding = yakuzaEmbedding };
        _ = db.Tags.Add(dummyTag);
        _ = db.Tags.Add(yakuzaTag);
        _ = await db.SaveChangesAsync();

        // Act: クエリ文字列の embedding を生成し、コサイン類似度でランク付けする
        var queryEmbedding = (await _embeddingService.GenerateEmbeddingAsync("反社会的勢力")).ToArray();

        System.Collections.Generic.List<(Tag Tag, float Similarity)> ranked = db.Tags.AsEnumerable()
            .Where(t => t.OwnerId == userId && t.Embedding != null && t.Embedding.Length == queryEmbedding.Length)
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