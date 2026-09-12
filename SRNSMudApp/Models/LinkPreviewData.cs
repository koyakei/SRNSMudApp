#region

using System.Diagnostics.CodeAnalysis;

#endregion

namespace SRNSMudApp.Models;

public class LinkPreviewData
{
    [SuppressMessage("Design", "CA1056")] public string Url { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    [SuppressMessage("Design", "CA1056")] public string ImageUrl { get; set; } = string.Empty;

    public string SiteName { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }

    /// <summary>
    ///     関連付けられているタグ情報のプレビュー一覧。
    /// </summary>
    public IReadOnlyList<TagPreviewItem> Tags { get; set; } = [];
}

/// <summary>
///     リンクプレビューで表示するためのタグ情報。
/// </summary>
/// <param name="Id">タグID。</param>
/// <param name="Name">タグ名。</param>
/// <param name="OwnerName">タグのオーナー名。</param>
/// <param name="Weight">アイテムにおけるタグの重み。</param>
public record TagPreviewItem(int Id, string Name, string? OwnerName, int Weight);