namespace SRNSMudApp.Client.Services;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using SRNSMudApp.Client.Models.Api;

/// <summary>
/// タグの取得・更新・削除・評価・フォロー・ロックなどの基本操作を提供する API クライアントインターフェース。
/// </summary>
public interface ITagApiClient
{
    /// <summary>
    /// タグの詳細情報を取得します。
    /// </summary>
    Task<TagDetailDto?> GetTagDetailAsync(int tagId, CancellationToken cancellationToken = default);

    /// <summary>
    /// タグ情報を更新します。
    /// </summary>
    Task<bool> UpdateTagAsync(int tagId, UpdateTagRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// タグを削除します。
    /// </summary>
    Task<bool> DeleteTagAsync(int tagId, CancellationToken cancellationToken = default);

    /// <summary>
    /// タグへの投票（アップボート / ダウンボート）を行います。
    /// </summary>
    Task<bool> VoteTagAsync(int tagId, bool isUpvote, CancellationToken cancellationToken = default);

    /// <summary>
    /// タグのフォロー状態を切り替えます。
    /// </summary>
    Task<bool> ToggleTagFollowAsync(int tagId, CancellationToken cancellationToken = default);

    /// <summary>
    /// タグのロック状態を切り替えます。
    /// </summary>
    Task<bool> ToggleTagLockAsync(int tagId, CancellationToken cancellationToken = default);

    /// <summary>
    /// タグ一覧（検索クエリによる絞り込み可）を取得します。
    /// </summary>
    Task<IReadOnlyList<TagDto>> GetTagListAsync(string? query = null, CancellationToken cancellationToken = default);
}