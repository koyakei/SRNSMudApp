namespace SRNSMudApp.Services.Push;

using SRNSMudApp.Models.Push;

/// <summary>
/// プッシュ通知サブスクリプション情報の保管と取得を抽象化するインターフェイス。
/// </summary>
public interface IPushSubscriptionStore
{
    /// <summary>
    /// サブスクリプション情報を登録または更新します。
    /// </summary>
    Task AddOrUpdateAsync(PushSubscriptionDto subscription, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたエンドポイントのサブスクリプションを削除します。
    /// </summary>
    Task RemoveAsync(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// 登録済みの全サブスクリプション情報を取得します。
    /// </summary>
    Task<IReadOnlyCollection<PushSubscriptionDto>> GetAllAsync(CancellationToken cancellationToken = default);
}