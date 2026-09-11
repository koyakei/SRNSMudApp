#pragma warning disable CA1848

using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Services;

/// <summary>
///     他ユーザーの Tag に対する「Content の編集提案」リクエストの申請、承認、却下、取り下げを管理するドメインサービス実装。
/// </summary>
public class TagContentProposalService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    INotificationService notificationService,
    ITagEmbeddingService tagEmbeddingService,
    ILogger<TagContentProposalService>? logger = null) : ITagContentProposalService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly INotificationService _notificationService =
        notificationService ?? throw new ArgumentNullException(nameof(notificationService));
    private readonly ITagEmbeddingService _tagEmbeddingService =
        tagEmbeddingService ?? throw new ArgumentNullException(nameof(tagEmbeddingService));
    private readonly ILogger<TagContentProposalService> _logger =
        logger ?? NullLogger<TagContentProposalService>.Instance;

    /// <inheritdoc />
    public async Task<Result<TagContentProposal>> ProposeContentAsync(
        int tagId,
        string proposedContent,
        string? reason,
        string requesterUserId,
        CancellationToken cancellationToken = default)
    {
        if (proposedContent is null)
        {
            return Result.Fail<TagContentProposal>("提案する内容が指定されていません。");
        }

        if (string.IsNullOrWhiteSpace(requesterUserId))
        {
            return Result.Fail<TagContentProposal>("リクエスト送信ユーザーが指定されていません。");
        }

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        Tag? tag = await context.Tags
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tagId, cancellationToken);

        if (tag is null)
        {
            return Result.Fail<TagContentProposal>("対象のタグが見つかりません。");
        }

        if (tag.OwnerId == requesterUserId)
        {
            return Result.Fail<TagContentProposal>("自分自身のタグに対しては編集提案ではなく直接編集を行ってください。");
        }

        if (string.Equals(tag.Content ?? string.Empty, proposedContent, StringComparison.Ordinal))
        {
            return Result.Fail<TagContentProposal>("提案内容が現在のタグの内容と同じです。");
        }

        // 同一タグ・同一提案内容・同一送信者による未処理提案（Proposed）の重複を防止
        bool duplicateExists = await context.TagContentProposals
            .AnyAsync(p => p.TagId == tagId &&
                           p.RequesterUserId == requesterUserId &&
                           p.ProposedContent == proposedContent &&
                           p.Status == TradeStatus.Proposed, cancellationToken);

        if (duplicateExists)
        {
            return Result.Fail<TagContentProposal>("この内容に対する編集提案は既に申請中です。");
        }

        var proposal = new TagContentProposal
        {
            TagId = tagId,
            RequesterUserId = requesterUserId,
            OwnerUserId = tag.OwnerId,
            ProposedContent = proposedContent,
            Reason = reason,
            Status = TradeStatus.Proposed,
            OwnerId = requesterUserId, // BaseEntity.OwnerId は作成者 (Requester)
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        _ = context.TagContentProposals.Add(proposal);
        _ = await context.SaveChangesAsync(cancellationToken);

        _notificationService.NotifyNotificationsChanged();

        return Result.Ok(proposal);
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Embedding generation failure should not block tag content proposal approval")]
    public async Task<Result<Tag>> ApproveProposalAsync(
        int proposalId,
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        TagContentProposal? proposal = await context.TagContentProposals
            .Include(p => p.Tag)
            .FirstOrDefaultAsync(p => p.Id == proposalId, cancellationToken);

        if (proposal is null)
        {
            return Result.Fail<Tag>("編集提案が見つかりません。");
        }

        if (proposal.OwnerUserId != ownerUserId)
        {
            return Result.Fail<Tag>("この提案を承認する権限がありません。");
        }

        if (proposal.Status != TradeStatus.Proposed)
        {
            return Result.Fail<Tag>($"申請中ではない提案は承認できません（現在の状態: {proposal.Status}）。");
        }

        Tag tag = proposal.Tag;
        if (tag is null)
        {
            return Result.Fail<Tag>("対象のタグが見つかりません。");
        }

        // 1. タグの Content 更新
        tag.Content = proposal.ProposedContent;
        tag.UpdatedDate = DateTime.UtcNow;

        // 2. ベクトル再生成（UpdateTagAsync と同様の処理）
        try
        {
            ReadOnlyMemory<float> embedding = await _tagEmbeddingService.GenerateEmbeddingAsync(tag.Name);
            tag.Embedding = embedding.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate embedding on proposal approval: {Message}", ex.Message);
        }

        // 3. 提案ステータス更新
        proposal.Status = TradeStatus.Executed;
        proposal.UpdatedDate = DateTime.UtcNow;

        _ = await context.SaveChangesAsync(cancellationToken);

        _notificationService.NotifyNotificationsChanged();

        return Result.Ok(tag);
    }

    /// <inheritdoc />
    public async Task<Result<bool>> RejectProposalAsync(
        int proposalId,
        string ownerUserId,
        string? rejectReason,
        CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        TagContentProposal? proposal = await context.TagContentProposals
            .FirstOrDefaultAsync(p => p.Id == proposalId, cancellationToken);

        if (proposal is null)
        {
            return Result.Fail("編集提案が見つかりません。");
        }

        if (proposal.OwnerUserId != ownerUserId)
        {
            return Result.Fail("この提案を却下する権限がありません。");
        }

        if (proposal.Status != TradeStatus.Proposed)
        {
            return Result.Fail($"申請中ではない提案は却下できません（現在の状態: {proposal.Status}）。");
        }

        proposal.Status = TradeStatus.Rejected;
        proposal.RejectReason = rejectReason;
        proposal.UpdatedDate = DateTime.UtcNow;

        _ = await context.SaveChangesAsync(cancellationToken);

        _notificationService.NotifyNotificationsChanged();

        return Result.Ok();
    }

    /// <inheritdoc />
    public async Task<Result<bool>> CancelProposalAsync(
        int proposalId,
        string requesterUserId,
        CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        TagContentProposal? proposal = await context.TagContentProposals
            .FirstOrDefaultAsync(p => p.Id == proposalId, cancellationToken);

        if (proposal is null)
        {
            return Result.Fail("編集提案が見つかりません。");
        }

        if (proposal.RequesterUserId != requesterUserId)
        {
            return Result.Fail("この提案を取り下げる権限がありません。");
        }

        if (proposal.Status != TradeStatus.Proposed)
        {
            return Result.Fail($"申請中ではない提案は取り下げできません（現在の状態: {proposal.Status}）。");
        }

        proposal.Status = TradeStatus.Canceled;
        proposal.UpdatedDate = DateTime.UtcNow;

        _ = await context.SaveChangesAsync(cancellationToken);

        _notificationService.NotifyNotificationsChanged();

        return Result.Ok();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TagContentProposal>> GetPendingProposalsForTagAsync(
        int tagId,
        CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        return await context.TagContentProposals
            .AsNoTracking()
            .Include(p => p.RequesterUser)
            .Include(p => p.OwnerUser)
            .Where(p => p.TagId == tagId && p.Status == TradeStatus.Proposed)
            .OrderByDescending(p => p.CreatedDate)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TagContentProposal?> GetProposalByIdAsync(
        int proposalId,
        CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        return await context.TagContentProposals
            .AsNoTracking()
            .Include(p => p.Tag)
            .Include(p => p.RequesterUser)
            .Include(p => p.OwnerUser)
            .FirstOrDefaultAsync(p => p.Id == proposalId, cancellationToken);
    }
}