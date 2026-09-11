using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Services;

/// <summary>
///     他ユーザーの Tag に対する「Name（タグ名）の編集提案」リクエストの申請、承認、却下、取り下げを管理するドメインサービス。
/// </summary>
public interface ITagNameProposalService
{
    /// <summary>
    ///     他ユーザーの Tag に対し、Name の編集提案リクエストを申請する。
    /// </summary>
    /// <param name="tagId">提案対象のタグID。</param>
    /// <param name="proposedName">提案する新しいタグ名。</param>
    /// <param name="reason">提案理由や補足（任意）。</param>
    /// <param name="requesterUserId">リクエスト申請者のユーザーID。</param>
    /// <param name="cancellationToken">キャンセラレーショントークン。</param>
    /// <returns>作成された提案エンティティまたはエラー結果。</returns>
    Task<Result<TagNameProposal>> ProposeNameAsync(
        int tagId,
        string proposedName,
        string? reason,
        string requesterUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     タグ所有者がタグ名編集提案を承認する。
    ///     Tag の Name が提案内容に更新され、ベクトルの再生成が行われる。
    /// </summary>
    /// <param name="proposalId">提案ID。</param>
    /// <param name="ownerUserId">タグ所有者のユーザーID。</param>
    /// <param name="cancellationToken">キャンセラレーショントークン。</param>
    /// <returns>更新されたタグエンティティまたはエラー結果。</returns>
    Task<Result<Tag>> ApproveProposalAsync(
        int proposalId,
        string ownerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     タグ所有者がタグ名編集提案を却下する。
    /// </summary>
    /// <param name="proposalId">提案ID。</param>
    /// <param name="ownerUserId">タグ所有者のユーザーID。</param>
    /// <param name="rejectReason">却下理由（任意）。</param>
    /// <param name="cancellationToken">キャンセラレーショントークン。</param>
    /// <returns>処理結果。</returns>
    Task<Result<bool>> RejectProposalAsync(
        int proposalId,
        string ownerUserId,
        string? rejectReason,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     提案送信者が申請中のタグ名編集提案を取り下げる。
    /// </summary>
    /// <param name="proposalId">提案ID。</param>
    /// <param name="requesterUserId">リクエスト申請者のユーザーID。</param>
    /// <param name="cancellationToken">キャンセラレーショントークン。</param>
    /// <returns>処理結果。</returns>
    Task<Result<bool>> CancelProposalAsync(
        int proposalId,
        string requesterUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     指定されたタグに対する保留中（Proposed）のタグ名編集提案一覧を取得する。
    /// </summary>
    /// <param name="tagId">タグID。</param>
    /// <param name="cancellationToken">キャンセラレーショントークン。</param>
    /// <returns>保留中の提案一覧。</returns>
    Task<IReadOnlyList<TagNameProposal>> GetPendingProposalsForTagAsync(
        int tagId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     指定されたタグ名編集提案を関連エンティティ込みで取得する。
    /// </summary>
    /// <param name="proposalId">提案ID。</param>
    /// <param name="cancellationToken">キャンセラレーショントークン。</param>
    /// <returns>提案エンティティ（存在しない場合は null）。</returns>
    Task<TagNameProposal?> GetProposalByIdAsync(
        int proposalId,
        CancellationToken cancellationToken = default);
}