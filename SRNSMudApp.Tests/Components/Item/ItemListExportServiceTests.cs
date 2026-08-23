using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

using Xunit;

namespace SRNSMudApp.Tests.Components.Item;

// 親名前空間 SRNSMudApp.Tests.Components の下にある namespace Item より先に
// Data.Item 型を解決させるため、エイリアスを名前空間の内側に置く
using Item = SRNSMudApp.Data.Item;
using Tag = SRNSMudApp.Data.Tag;

/// <summary>
///     ItemListExportService の単体テスト。
///     タグ展開 (親タグ・関連タグ) とリンクプレビュー組み立てを bUnit なしで検証する。
///     LinkPreviewService は具象クラスのため、フェイク HTTP ハンドラ経由の実インスタンスを注入する。
/// </summary>
public class ItemListExportServiceTests
{
    [Fact]
    public async Task BuildExportAsync_WithUnknownTag_SkipsRelation()
    {
        var exportData = new ItemListExportData(
            AllTags: [],
            ItemTagRelations: [new TagRelation { ItemId = 1, TagId = 99, OwnerId = "u1" }],
            TagToTagRelations: []);
        var service = new ItemListExportService(CreatePreviewService());

        IReadOnlyList<ExportItemDto> result = await service.BuildExportAsync(
            exportData, [new Item { Id = 1, Content = "content", OwnerId = "u1" }]);

        ExportItemDto dto = Assert.Single(result);
        Assert.Empty(dto.Tags);
    }

    [Fact]
    public async Task BuildExportAsync_WithParentAndRelatedTags_ExpandsHierarchy()
    {
        var parent = new Tag { Id = 1, Name = "Parent", OwnerId = "u1" };
        var child = new Tag { Id = 2, Name = "Child", ParentTagId = 1, OwnerId = "u1" };
        var related = new Tag { Id = 3, Name = "Related", OwnerId = "u1" };
        var exportData = new ItemListExportData(
            AllTags: new Dictionary<int, Tag> { [1] = parent, [2] = child, [3] = related },
            ItemTagRelations: [new TagRelation { ItemId = 1, TagId = 2, OwnerId = "u1" }],
            TagToTagRelations: [new TagRelationToTag { TagId = 3, TargetTagId = 2, OwnerId = "u1" }]);
        var service = new ItemListExportService(CreatePreviewService());

        IReadOnlyList<ExportItemDto> result = await service.BuildExportAsync(
            exportData, [new Item { Id = 1, Content = "content", OwnerId = "u1" }]);

        ExportItemDto dto = Assert.Single(result);
        ExportTagDto tag = Assert.Single(dto.Tags);
        Assert.Equal("Child", tag.Name);
        Assert.Equal(["Parent"], tag.ParentTags.Select(t => t.Name));
        Assert.Equal(["Related"], tag.RelatedTags.Select(t => t.Name));
    }

    [Fact]
    public async Task BuildExportAsync_WithSuccessfulPreviews_IncludesMaxThree()
    {
        const string content = "https://a.com https://b.com https://c.com https://d.com";
        var exportData = new ItemListExportData([], [], []);
        var service = new ItemListExportService(CreatePreviewService());

        IReadOnlyList<ExportItemDto> result =
            await service.BuildExportAsync(exportData, [new Item { Id = 1, Content = content, OwnerId = "u1" }]);

        ExportItemDto dto = Assert.Single(result);
        Assert.Equal(3, dto.LinkPreviews.Count);
        Assert.All(dto.LinkPreviews, lp => Assert.False(string.IsNullOrWhiteSpace(lp.Title)));
    }

    [Fact]
    public async Task BuildExportAsync_WithFailingFetch_ExcludesPreview()
    {
        var exportData = new ItemListExportData([], [], []);
        // 404 を返すハンドラでプレビュー取得失敗を再現する
        var service = new ItemListExportService(new LinkPreviewService(
            new HttpClient(new StatusCodeHandler(HttpStatusCode.NotFound))));

        IReadOnlyList<ExportItemDto> result = await service.BuildExportAsync(
            exportData, [new Item { Id = 1, Content = "see https://fail.com", OwnerId = "u1" }]);

        ExportItemDto dto = Assert.Single(result);
        Assert.Empty(dto.LinkPreviews);
    }

    [Fact]
    public void Serialize_ProducesIndentedJsonWithUnicode()
    {
        var items = new List<ExportItemDto>
        {
            new()
            {
                Content = "日本語コンテンツ",
                Tags = [new ExportTagDto { Name = "タグ", ParentTags = [new ExportTagSimpleDto { Name = "親" }] }]
            }
        };

        var json = ItemListExportService.Serialize(items);

        Assert.Contains("\n", json);                       // インデント付き
        Assert.Contains("日本語コンテンツ", json);           // Unicode がそのまま出力される
        Assert.Contains("\"ParentTags\"", json);
    }

    /// <summary>あらゆる GET に title 付き HTML を返すフェイクハンドラ。</summary>
    private static LinkPreviewService CreatePreviewService() =>
        new(new HttpClient(new TitleHtmlHandler()));

    private sealed class TitleHtmlHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "<html><head><title>Example Title</title></head><body></body></html>",
                    System.Text.Encoding.UTF8,
                    "text/html")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class StatusCodeHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(statusCode));
    }
}