namespace SRNSMudApp.Services;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// サーバー/API層における重複リクエスト・多重処理を防止するサービスインターフェース。
/// </summary>
public interface IRequestDeduplicationService
{
    /// <summary>
    /// 指定されたキーに対する操作の排他実行ロックを取得します。
    /// </summary>
    Task<IDisposable> AcquireLockAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたキーが直近で既に完了しているか判定し、キャッシュされた結果（存在する場合）を取得します。
    /// </summary>
    bool TryGetRecentResult<T>(string key, out T? result);

    /// <summary>
    /// 完了した操作の結果をキーに関連付けてキャッシュします。
    /// </summary>
    void RecordResult<T>(string key, T result, TimeSpan? retention = null);
}