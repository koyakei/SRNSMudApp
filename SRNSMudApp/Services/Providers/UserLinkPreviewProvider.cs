using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;

namespace SRNSMudApp.Services.Providers;

/// <summary>
///     /User/UserDetail/{id} 形式の内部ユーザーリンクに対応し、
///     <see cref="IDbContextFactory{ApplicationDbContext}"/> 経由でユーザー情報を取得してプレビューを生成するプロバイダー。
/// </summary>
public class UserLinkPreviewProvider(IDbContextFactory<ApplicationDbContext> dbFactory) : ILinkPreviewProvider
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    /// <inheritdoc />
    public int Order => 30;

    /// <inheritdoc />
    public bool CanHandle(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var path = GetNormalizedPath(uri);
        return path.StartsWith("/User/UserDetail/", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1054:URI parameters should not be strings", Justification = "Original URL representation")]
    public async Task<LinkPreviewData> GetPreviewAsync(Uri uri, string originalUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(originalUrl);

        var path = GetNormalizedPath(uri);
        var userId = path["/User/UserDetail/".Length..];

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null)
        {
            return new LinkPreviewData { Url = originalUrl, IsSuccess = false };
        }

        return new LinkPreviewData
        {
            Url = originalUrl,
            Title = $"User: {user.UserName}",
            Description = $"@{user.UserName}",
            DisplayText = $"@{user.UserName}",
            SiteName = "SRNSMudApp",
            IsSuccess = true
        };
    }

    private static string GetNormalizedPath(Uri uri) =>
        uri.IsAbsoluteUri ? uri.AbsolutePath : uri.ToString().Split('?')[0];
}