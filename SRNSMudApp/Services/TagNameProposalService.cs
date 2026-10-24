#pragma warning disable CA1848

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Services;

/// <summary>
///     他ユーザーの Tag に対する「Name の編集提案」リクエストの申請、承認、却下、取り下げを管理するドメインサービス実装。
/// </summary>
public partial class TagNameProposalService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    INotificationService notificationService,
    ITagEmbeddingService tagEmbeddingService,
    ITagLockService? tagLockService = null,
    ILogger<TagNameProposalService>? logger = null) : ITagNameProposalService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly INotificationService _notificationService =
        notificationService ?? throw new ArgumentNullException(nameof(notificationService));
    private readonly ITagEmbeddingService _tagEmbeddingService =
        tagEmbeddingService ?? throw new ArgumentNullException(nameof(tagEmbeddingService));
    private readonly ITagLockService? _tagLockService = tagLockService;
    private readonly ILogger<TagNameProposalService> _logger =
        logger ?? NullLogger<TagNameProposalService>.Instance;

    [GeneratedRegex(@"^[\x20-\x7E\u3000-\u30FF\u4E00-\u9FFF\uFF01-\uFF9F\u2200-\u22FF]+$")]
    public static partial Regex TagNameRegex();

    /// <inheritdoc />
    public async Task<Result<TagNameProposal>> ProposeNameAsync(
        int tagId,
        string proposedName,
        string? reason,
        string requesterUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(proposedName))
        {
            return Result.Fail<TagNameProposal>("提案するタグ名が指定されていません。");
        }

        proposedName = proposedName.Trim();

        if (proposedName.Length > 100)
        {
            return Result.Fail<TagNameProposal>("タグ名は100文字以内で入力してください。");
        }

        if (!TagNameRegex().IsMatch(proposedName))
        {
            return Result.Fail<TagNameProposal>("タグ名には漢字、ひらがな、カタカナ（半角/全角）、英数字（半角/全角）、アンダーバー、数学記号のみ使用できます。");
        }

        if (Tag.VoteTagNames.Contains(proposedName) ||
            Tag.ReactionTagNames.Contains(proposedName) ||
            string.Equals(proposedName, Tag.RootTagName, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Fail<TagNameProposal>("予約されているシステムタグ名に変更することはできません。");
        }

        if (string.IsNullOrWhiteSpace(requesterUserId))
        {
            return Result.Fail<TagNameProposal>("リクエスト送信ユーザーが指定されていません。");
        }

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        Tag? tag = await context.Tags
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tagId, cancellationToken);

        if (tag is null)
        {
            return Result.Fail<TagNameProposal>("対象のタグが見つかりません。");
        }

        if (tag.Name == Tag.RootTagName || tag.IsSystem || tag.OwnerId == "system")
        {
            return Result.Fail<TagNameProposal>("ルートタグやシステムタグの名前変更提案はできません。");
        }

        if (_tagLockService != null)
        {
            var isLocked = await _tagLockService.IsTagOrSiblingLockedAsync(tagId, cancellationToken);
            if (isLocked)
            {
                return Result.Fail<TagNameProposal>("ロックされているタグまたはその兄弟タグは編集提案できません。");
            }
        }

        if (tag.OwnerId == requesterUserId)
        {
            return Result.Fail<TagNameProposal>("自分自身のタグに対しては編集提案ではなく直接編集を行ってください。");
        }

        if (string.Equals(tag.Name, proposedName, StringComparison.Ordinal))
        {
            return Result.Fail<TagNameProposal>("提案された名前が現在のタグ名と同じです。");
        }

        // タグ所有者が既にその名前のタグを所有していないか確認
        bool nameExistsInOwner = await context.Tags
            .AnyAsync(t => t.OwnerId == tag.OwnerId && t.Name == proposedName, cancellationToken);

        if (nameExistsInOwner)
        {
            return Result.Fail<TagNameProposal>("タグの所有者は既に同名のタグを所持しています。");
        }

        // 同一タグ・同一提案名・同一送信者による未処理提案（Proposed）の重複を防止
        bool duplicateExists = await context.TagNameProposals
            .AnyAsync(p => p.TagId == tagId &&
                           p.RequesterUserId == requesterUserId &&
                           p.ProposedName == proposedName &&
                           p.Status == TradeStatus.Proposed, cancellationToken);

        if (duplicateExists)
        {
            return Result.Fail<TagNameProposal>("この名前に対する編集提案は既に申請中です。");
        }

        var proposal = new TagNameProposal
        {
            TagId = tagId,
            RequesterUserId = requesterUserId,
            OwnerUserId = tag.OwnerId,
            ProposedName = proposedName,
            Reason = reason,
            Status = TradeStatus.Proposed,
            OwnerId = requesterUserId, // BaseEntity.OwnerId は作成者 (Requester)
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        _ = context.TagNameProposals.Add(proposal);
        _ = await context.SaveChangesAsync(cancellationToken);

        _notificationService.NotifyNotificationsChanged();

        return Result.Ok(proposal);
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Embedding generation failure should not block tag name proposal approval")]
    public async Task<Result<Tag>> ApproveProposalAsync(
        int proposalId,
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        TagNameProposal? proposal = await context.TagNameProposals
            .Include(p => p.Tag)
            .FirstOrDefaultAsync(p => p.Id == proposalId, cancellationToken);

        if (proposal is null)
        {
            return Result.Fail<Tag>("編集提案が見つかりません。");
        }

        if (!proposal.CanBeApprovedBy(ownerUserId))
        {
            if (proposal.OwnerUserId != ownerUserId)
            {
                return Result.Fail<Tag>("この提案を承認する権限がありません。");
            }

            return Result.Fail<Tag>($"申請中ではない提案は承認できません（現在の状態: {proposal.Status}）。");
        }

        Tag tag = proposal.Tag;
        if (tag is null)
        {
            return Result.Fail<Tag>("対象のタグが見つかりません。");
        }

        if (_tagLockService != null)
        {
            var isLocked = await _tagLockService.IsTagOrSiblingLockedAsync(tag.Id, cancellationToken);
            if (isLocked)
            {
                return Result.Fail<Tag>("ロックされているタグまたはその兄弟タグは編集できません。");
            }
        }

        // 承認時点でも同名の他タグが存在しないか確認（ユニーク制約の衝突回避）
        bool nameConflict = await context.Tags
            .AnyAsync(t => t.OwnerId == ownerUserId && t.Name == proposal.ProposedName && t.Id != tag.Id, cancellationToken);

        if (nameConflict)
        {
            return Result.Fail<Tag>("同名のタグが既に存在するため承認できません。");
        }

        // 1. タグの Name 更新
        tag.Name = proposal.ProposedName;
        tag.UpdatedDate = DateTime.UtcNow;

        // 2. ベクトル再生成
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
        _ = proposal.Approve();

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

        TagNameProposal? proposal = await context.TagNameProposals
            .FirstOrDefaultAsync(p => p.Id == proposalId, cancellationToken);

        if (proposal is null)
        {
            return Result.Fail("編集提案が見つかりません。");
        }

        if (!proposal.CanBeRejectedBy(ownerUserId))
        {
            if (proposal.OwnerUserId != ownerUserId)
            {
                return Result.Fail("この提案を却下する権限がありません。");
            }

            return Result.Fail($"申請中ではない提案は却下できません（現在の状態: {proposal.Status}）。");
        }

        _ = proposal.Reject(rejectReason);

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

        TagNameProposal? proposal = await context.TagNameProposals
            .FirstOrDefaultAsync(p => p.Id == proposalId, cancellationToken);

        if (proposal is null)
        {
            return Result.Fail("編集提案が見つかりません。");
        }

        if (!proposal.CanBeCanceledBy(requesterUserId))
        {
            if (proposal.RequesterUserId != requesterUserId)
            {
                return Result.Fail("この提案を取り下げる権限がありません。");
            }

            return Result.Fail($"申請中ではない提案は取り下げできません（現在の状態: {proposal.Status}）。");
        }

        _ = proposal.Cancel();

        _ = await context.SaveChangesAsync(cancellationToken);

        _notificationService.NotifyNotificationsChanged();

        return Result.Ok();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TagNameProposal>> GetPendingProposalsForTagAsync(
        int tagId,
        CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        return await context.TagNameProposals
            .AsNoTracking()
            .Include(p => p.RequesterUser)
            .Include(p => p.OwnerUser)
            .Where(p => p.TagId == tagId && p.Status == TradeStatus.Proposed)
            .OrderByDescending(p => p.CreatedDate)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TagNameProposal?> GetProposalByIdAsync(
        int proposalId,
        CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        return await context.TagNameProposals
            .AsNoTracking()
            .Include(p => p.Tag)
            .Include(p => p.RequesterUser)
            .Include(p => p.OwnerUser)
            .FirstOrDefaultAsync(p => p.Id == proposalId, cancellationToken);
    }
}