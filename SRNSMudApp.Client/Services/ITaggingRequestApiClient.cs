namespace SRNSMudApp.Client.Services;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using SRNSMudApp.Client.Models.Api;

/// <summary>
/// アイテムへのタグ付けリクエストの取得・承認・却下を行う API クライアントインターフェース。
/// </summary>
public interface ITaggingRequestApiClient
{
    /// <summary>
    /// 特定アイテムに対するタグ付けリクエスト一覧を取得します。
    /// </summary>
    Task<IReadOnlyList<TaggingRequestDto>> GetTaggingRequestsAsync(int itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// タグ付けリクエストを承認します。
    /// </summary>
    Task<bool> ApproveTaggingRequestAsync(int requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// タグ付けリクエストを却下します。
    /// </summary>
    Task<bool> RejectTaggingRequestAsync(int requestId, RejectTaggingRequest request, CancellationToken cancellationToken = default);
}