using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;

namespace SRNSMudApp.Services.Providers;

/// <summary>
///     /TagDetail/{id} 形式の内部タグリンクに対応し、
///     <see cref="IDbContextFactory{ApplicationDbContext}"/> 経由でタグ情報を取得してプレビューを生成するプロバイダー。
/// </summary>
public partial class TagLinkPreviewProvider(IDbContextFactory<ApplicationDbContext> dbFactory) : ILinkPreviewProvider
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    /// <inheritdoc />
    public int Order => 20;

    /// <inheritdoc />
    public bool CanHandle(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var path = GetNormalizedPath(uri);
        return path.StartsWith("/TagDetail/", StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(path.AsSpan("/TagDetail/".Length), out _);
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1054:URI parameters should not be strings", Justification = "Original URL representation")]
    public async Task<LinkPreviewData> GetPreviewAsync(Uri uri, string originalUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(originalUrl);

        var path = GetNormalizedPath(uri);
        if (!int.TryParse(path.AsSpan("/TagDetail/".Length), out int tagId))
        {
            return new LinkPreviewData { Url = originalUrl, IsSuccess = false };
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var tag = await db.Tags
            .AsNoTracking()
            .Include(t => t.Owner)
            .FirstOrDefaultAsync(t => t.Id == tagId, cancellationToken);

        if (tag == null)
        {
            return new LinkPreviewData { Url = originalUrl, IsSuccess = false };
        }

        var text = string.IsNullOrWhiteSpace(tag.Content) ? "タグ詳細" : WhitespaceRegex().Replace(tag.Content, " ").Trim();
        if (text.Length > 200)
        {
            text = $"{text.AsSpan(0, 200)}...";
        }

        var ownerName = tag.Owner?.UserName ?? (tag.GetKind() is Models.Unions.SystemClassificationTag ? "system" : "unknown");

        return new LinkPreviewData
        {
            Url = originalUrl,
            Title = $"Tag: {tag.Name} ({ownerName})",
            Description = text,
            DisplayText = $"{tag.Name}:{ownerName}",
            Tags = [new TagPreviewItem(tag.Id, tag.Name, ownerName, 0)],
            SiteName = "SRNSMudApp",
            IsSuccess = true
        };
    }

    private static string GetNormalizedPath(Uri uri) =>
        uri.IsAbsoluteUri ? uri.AbsolutePath : uri.ToString().Split('?')[0];

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}