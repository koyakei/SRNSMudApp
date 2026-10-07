using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;

namespace SRNSMudApp.Services.Providers;

/// <summary>
///     /ItemDetail/{id} 形式の内部アイテムリンクに対応し、
///     <see cref="IDbContextFactory{ApplicationDbContext}"/> 経由でアイテム情報を取得してプレビューを生成するプロバイダー。
/// </summary>
public partial class ItemLinkPreviewProvider(IDbContextFactory<ApplicationDbContext> dbFactory) : ILinkPreviewProvider
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    /// <inheritdoc />
    public int Order => 10;

    /// <inheritdoc />
    public bool CanHandle(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var path = GetNormalizedPath(uri);
        return path.StartsWith("/ItemDetail/", StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(path.AsSpan("/ItemDetail/".Length), out _);
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1054:URI parameters should not be strings", Justification = "Original URL representation")]
    public async Task<LinkPreviewData> GetPreviewAsync(Uri uri, string originalUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(originalUrl);

        var path = GetNormalizedPath(uri);
        if (!int.TryParse(path.AsSpan("/ItemDetail/".Length), out int itemId))
        {
            return new LinkPreviewData { Url = originalUrl, IsSuccess = false };
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var item = await db.Items
            .AsNoTracking()
            .Include(i => i.TagRelations)
                .ThenInclude(tr => tr.Tag)
                    .ThenInclude(t => t.Owner)
            .FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);

        if (item == null)
        {
            return new LinkPreviewData { Url = originalUrl, IsSuccess = false };
        }

        var tags = item.TagRelations
            .Where(tr => tr.Tag != null && !tr.Tag.IsSystem)
            .OrderByDescending(tr => tr.Weight)
            .Select(tr =>
            {
                var ownerName = tr.Tag.Owner?.UserName ?? (tr.Tag.GetKind() is Models.Unions.SystemClassificationTag ? "system" : "unknown");
                return new TagPreviewItem(tr.Tag.Id, tr.Tag.Name, ownerName, tr.Weight);
            })
            .ToList();

        var text = WhitespaceRegex().Replace(item.Content ?? "", " ").Trim();
        if (text.Length > 200)
        {
            text = $"{text.AsSpan(0, 200)}...";
        }

        return new LinkPreviewData
        {
            Url = originalUrl,
            Title = $"Item #{item.Id}",
            Description = text,
            Tags = tags,
            SiteName = "SRNSMudApp",
            IsSuccess = true
        };
    }

    private static string GetNormalizedPath(Uri uri) =>
        uri.IsAbsoluteUri ? uri.AbsolutePath : uri.ToString().Split('?')[0];

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}