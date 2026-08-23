using SRNSMudApp.Components.UI;

namespace SRNSMudApp.Services;

public interface ISystemTagEnsurer
{
    /// <summary>
    ///     必要なら投票用システムタグ (good / bad) を作成し、確定したタグ ID を返す。
    ///     <c>Refetch</c> が true の場合、呼び出し元はタグ一覧を再取得して ID を再計算すること。
    /// </summary>
    Task<(SystemTagIds Ids, bool Refetch)> EnsureAsync(string? userId, SystemTagIds current);
}

/// <summary>
///     投票用システムタグの存在保証ロジックの共通実装。
///     Home / ResourceList / NotificationsPage で重複していたガードと分岐を集約する (Facade)。
/// </summary>
public class SystemTagEnsurer(IHomeDataProvider homeData) : ISystemTagEnsurer
{
    public async Task<(SystemTagIds Ids, bool Refetch)> EnsureAsync(string? userId, SystemTagIds current)
    {
        if (string.IsNullOrEmpty(userId) || current.IsComplete)
        {
            return (current, false);
        }

        SystemTagsResult result = await homeData.EnsureSystemTagsAsync(userId);

        return result.Created
            ? (current, true)
            : (new SystemTagIds(result.GoodTagId, result.BadTagId), false);
    }
}