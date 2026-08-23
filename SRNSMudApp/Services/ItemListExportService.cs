using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models;

namespace SRNSMudApp.Services;

/// <summary>JSON エクスポート 1 アイテム分の DTO。</summary>
public sealed record ExportItemDto
{
    public string Content { get; init; } = string.Empty;
    public IReadOnlyList<ExportTagDto> Tags { get; init; } = [];
    public IReadOnlyList<ExportLinkPreviewDto> LinkPreviews { get; init; } = [];
}

/// <summary>JSON エクスポート用のタグ DTO (親タグ・関連タグを含む)。</summary>
public sealed record ExportTagDto
{
    public string Name { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public IReadOnlyList<ExportTagSimpleDto> ParentTags { get; init; } = [];
    public IReadOnlyList<ExportTagSimpleDto> RelatedTags { get; init; } = [];
}

/// <summary>JSON エクスポート用の簡易タグ DTO。</summary>
public sealed record ExportTagSimpleDto
{
    public string Name { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
}

/// <summary>JSON エクスポート用のリンクプレビュー DTO。</summary>
public sealed record ExportLinkPreviewDto
{
    [SuppressMessage("Design", "CA1056")]
    public string Url { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    [SuppressMessage("Design", "CA1056")]
    public string ImageUrl { get; init; } = string.Empty;

    public string SiteName { get; init; } = string.Empty;
}

/// <summary>
///     ItemList の JSON エクスポート構築をコンポーネントから分離するインターフェース。
/// </summary>
public interface IItemListExportService
{
    /// <summary>生データとアイテム一覧からエクスポート DTO を組み立てる。</summary>
    Task<IReadOnlyList<ExportItemDto>> BuildExportAsync(ItemListExportData exportData, IEnumerable<Item> items);
}

/// <summary>
///     タグラベルの解決・親タグ辿り・関連タグ収集・リンクプレビュー取得を行い、
///     JSON エクスポート用 DTO 群を構築するアプリケーションサービス。
///     UI (JS ダウンロード) 以外の処理はすべてここに集約し、bUnit 不要の単体テストを可能にする。
/// </summary>
public sealed class ItemListExportService(LinkPreviewService linkPreviewService) : IItemListExportService
{
    private const int MaxLinkPreviewsPerItem = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    /// <summary>エクスポート DTO 群をダウンロード用の JSON 文字列へシリアライズする。</summary>
    public static string Serialize(IReadOnlyList<ExportItemDto> exportItems) =>
        JsonSerializer.Serialize(exportItems, JsonOptions);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExportItemDto>> BuildExportAsync(
        ItemListExportData exportData,
        IEnumerable<Item> items)
    {
        Dictionary<int, Tag> allTags = exportData.AllTags;
        List<ExportItemDto> exportList = [];

        foreach (Item item in items)
        {
            List<TagRelation> relations =
                [.. exportData.ItemTagRelations.Where(tr => tr.ItemId == item.Id)];

            List<ExportTagDto> tags = [.. relations.Select(rel => BuildExportTag(rel, allTags, exportData.TagToTagRelations))
                .OfType<ExportTagDto>()];

            exportList.Add(new ExportItemDto
            {
                Content = item.Content,
                Tags = tags,
                LinkPreviews = await BuildLinkPreviewsAsync(item.Content)
            });
        }

        return exportList;
    }

    /// <summary>1 件のタグ関連付けを、親タグ・関連タグを展開した DTO へ変換する。タグ不明時は null。</summary>
    private static ExportTagDto? BuildExportTag(
        TagRelation relation,
        Dictionary<int, Tag> allTags,
        IReadOnlyCollection<TagRelationToTag> tagToTags)
    {
        return !allTags.TryGetValue(relation.TagId, out Tag? tag)
            ? null
            : new ExportTagDto
            {
                Name = tag.Name,
                Content = tag.Content,
                ParentTags = [.. CollectParentTags(tag, allTags)],
                RelatedTags = [.. CollectRelatedTags(tag.Id, allTags, tagToTags)]
            };
    }

    private static IEnumerable<ExportTagSimpleDto> CollectParentTags(Tag tag, Dictionary<int, Tag> allTags)
    {
        Tag current = tag;
        while (current.ParentTagId is int parentId && allTags.TryGetValue(parentId, out Tag? parent))
        {
            yield return new ExportTagSimpleDto { Name = parent.Name, Content = parent.Content };
            current = parent;
        }
    }

    private static IEnumerable<ExportTagSimpleDto> CollectRelatedTags(
        int tagId,
        Dictionary<int, Tag> allTags,
        IReadOnlyCollection<TagRelationToTag> tagToTags)
    {
        IEnumerable<int> relatedIds = tagToTags.Where(trt => trt.TargetTagId == tagId).Select(trt => trt.TagId);

        return relatedIds
            .Select(id => allTags.TryGetValue(id, out Tag? attached) ? ToSimpleDto(attached) : null)
            .OfType<ExportTagSimpleDto>();
    }

    private static ExportTagSimpleDto ToSimpleDto(Tag tag) =>
        new() { Name = tag.Name, Content = tag.Content };

    private async Task<List<ExportLinkPreviewDto>> BuildLinkPreviewsAsync(string? content)
    {
        IEnumerable<string> urls = ItemCardViewModel.ExtractUrls(content).Take(MaxLinkPreviewsPerItem);
        ExportLinkPreviewDto?[] previews = await Task.WhenAll(urls.Select(async url =>
        {
            LinkPreviewData preview = await linkPreviewService.GetPreviewAsync(url);
            return preview.IsSuccess ? ToDto(preview) : null;
        }));

        return [.. previews.OfType<ExportLinkPreviewDto>()];
    }

    private static ExportLinkPreviewDto ToDto(LinkPreviewData preview) =>
        new()
        {
            Url = preview.Url,
            Title = preview.Title,
            Description = preview.Description,
            ImageUrl = preview.ImageUrl,
            SiteName = preview.SiteName
        };
}