#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.Services.Reports;

/// <summary>
///     通報対象エンティティ（Item, Tag など）ごとのスナップショット取得および削除処理を抽象化する Strategy インターフェース。
///     新しい通報対象エンティティを追加する際は、本インターフェースの実装を追加して DI に登録することで対応可能（OCP 準拠）。
/// </summary>
public interface IReportTargetHandler
{
    /// <summary>対応する通報対象種別。</summary>
    ReportTargetType TargetType { get; }

    /// <summary>
    ///     対象エンティティの存在確認を行い、通報時スナップショット文字列を生成する。
    /// </summary>
    /// <param name="context">データベースコンテキスト。</param>
    /// <param name="targetId">対象エンティティの ID。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>通報時スナップショット文字列。</returns>
    Task<string> CaptureSnapshotAsync(ApplicationDbContext context, int targetId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     対象エンティティをデータベースから削除する。
    /// </summary>
    /// <param name="context">データベースコンテキスト。</param>
    /// <param name="targetId">対象エンティティの ID。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    Task DeleteTargetAsync(ApplicationDbContext context, int targetId, CancellationToken cancellationToken = default);
}

/// <summary>
///     アイテム（Item）に対する通報処理 Strategy。
/// </summary>
public class ItemReportTargetHandler : IReportTargetHandler
{
    /// <inheritdoc />
    public ReportTargetType TargetType => ReportTargetType.Item;

    /// <inheritdoc />
    public async Task<string> CaptureSnapshotAsync(ApplicationDbContext context, int targetId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        Item? item = await context.Items
            .Include(i => i.Owner)
            .FirstOrDefaultAsync(i => i.Id == targetId, cancellationToken);

        if (item is null)
        {
            throw new InvalidOperationException($"通報対象のアイテム (ID: {targetId}) が見つかりません。");
        }

        var snapshot = $"[投稿者: {item.Owner?.UserName ?? "不明"}] {item.Content}";
        return snapshot.Length > 2000 ? snapshot[..1997] + "..." : snapshot;
    }

    /// <inheritdoc />
    public async Task DeleteTargetAsync(ApplicationDbContext context, int targetId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        Item? item = await context.Items.FindAsync([targetId], cancellationToken);
        if (item is not null)
        {
            _ = context.Items.Remove(item);
        }
    }
}

/// <summary>
///     タグ（Tag）に対する通報処理 Strategy。
/// </summary>
public class TagReportTargetHandler : IReportTargetHandler
{
    /// <inheritdoc />
    public ReportTargetType TargetType => ReportTargetType.Tag;

    /// <inheritdoc />
    public async Task<string> CaptureSnapshotAsync(ApplicationDbContext context, int targetId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        Tag? tag = await context.Tags
            .Include(t => t.Owner)
            .FirstOrDefaultAsync(t => t.Id == targetId, cancellationToken);

        if (tag is null)
        {
            throw new InvalidOperationException($"通報対象のタグ (ID: {targetId}) が見つかりません。");
        }

        var snapshot = $"[タグ名: {tag.Name} / 作成者: {tag.Owner?.UserName ?? "不明"}] {tag.Content}";
        return snapshot.Length > 2000 ? snapshot[..1997] + "..." : snapshot;
    }

    /// <inheritdoc />
    public async Task DeleteTargetAsync(ApplicationDbContext context, int targetId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        Tag? tag = await context.Tags.FindAsync([targetId], cancellationToken);
        if (tag is not null)
        {
            _ = context.Tags.Remove(tag);
        }
    }
}

/// <summary>
///     通報対象種別に応じた <see cref="IReportTargetHandler" /> を解決する Factory インターフェース。
/// </summary>
public interface IReportTargetHandlerFactory
{
    /// <summary>
    ///     指定された対象種別に対応するハンドラーを取得する。
    /// </summary>
    /// <param name="targetType">通報対象種別。</param>
    /// <returns>対応する Strategy 実装。</returns>
    IReportTargetHandler GetHandler(ReportTargetType targetType);
}

/// <summary>
///     <see cref="IReportTargetHandlerFactory" /> の既定実装。
/// </summary>
public class ReportTargetHandlerFactory(IEnumerable<IReportTargetHandler> handlers) : IReportTargetHandlerFactory
{
    private readonly Dictionary<ReportTargetType, IReportTargetHandler> _handlers =
        handlers?.ToDictionary(h => h.TargetType) ?? throw new ArgumentNullException(nameof(handlers));

    /// <inheritdoc />
    public IReportTargetHandler GetHandler(ReportTargetType targetType)
    {
        if (_handlers.TryGetValue(targetType, out IReportTargetHandler? handler))
        {
            return handler;
        }

        throw new NotSupportedException($"未対応の通報対象種別です: {targetType}");
    }
}