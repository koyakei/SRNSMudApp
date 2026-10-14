namespace SRNSMudApp.Client.Services;

using System.Threading;
using System.Threading.Tasks;

using SRNSMudApp.Client.Models.Api;

/// <summary>
/// タグ名・タグ説明文の変更提案およびその承認・却下・取り下げを扱う API クライアントインターフェース。
/// </summary>
public interface ITagProposalApiClient
{
    /// <summary>
    /// タグ名変更の提案を送信します。
    /// </summary>
    Task<TagNameProposalDto?> ProposeTagNameAsync(ProposeTagNameRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// タグ説明文変更の提案を送信します。
    /// </summary>
    Task<TagContentProposalDto?> ProposeTagContentAsync(ProposeTagContentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 特定タグに対する保留中の変更提案一覧を取得します。
    /// </summary>
    Task<TagPendingProposalsDto?> GetPendingProposalsForTagAsync(int tagId, CancellationToken cancellationToken = default);

    /// <summary>
    /// タグ名変更提案を承認します。
    /// </summary>
    Task<bool> ApproveTagNameProposalAsync(int proposalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// タグ名変更提案を却下します。
    /// </summary>
    Task<bool> RejectTagNameProposalAsync(int proposalId, RejectProposalRequest? request = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// タグ説明文変更提案を承認します。
    /// </summary>
    Task<bool> ApproveTagContentProposalAsync(int proposalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// タグ説明文変更提案を却下します。
    /// </summary>
    Task<bool> RejectTagContentProposalAsync(int proposalId, RejectProposalRequest? request = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 自分が送信したタグ名変更提案を取り下げます。
    /// </summary>
    Task<bool> CancelTagNameProposalAsync(int proposalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 自分が送信したタグ説明文変更提案を取り下げます。
    /// </summary>
    Task<bool> CancelTagContentProposalAsync(int proposalId, CancellationToken cancellationToken = default);
}