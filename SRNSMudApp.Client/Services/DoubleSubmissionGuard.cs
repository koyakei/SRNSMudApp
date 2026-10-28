namespace SRNSMudApp.Client.Services;

using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>
/// フォーム送信やデータ更新ボタンの連続クリックによる二重送信を防止し、
/// 処理中および直近の重複操作を検知するガードクラス。
/// </summary>
public interface IDoubleSubmissionGuard
{
    /// <summary>
    /// 現在リクエストが処理中かどうかを取得します。
    /// </summary>
    bool IsSubmitting { get; }

    /// <summary>
    /// 送信の開始を試行します。
    /// 既に処理中である場合、または直近に同一操作キーで完了している場合は false を返します。
    /// </summary>
    /// <param name="operationKey">操作を一意に識別するキー（本文やターゲットIDなど）。省略時は処理中のみ判定。</param>
    /// <returns>送信を開始できる場合は true、二重送信として抑止された場合は false。</returns>
    bool TryBeginSubmit(string? operationKey = null);

    /// <summary>
    /// 送信処理の正常完了を通知し、直近の重複防止ウィンドウを開始します。
    /// </summary>
    /// <param name="operationKey">操作を一意に識別するキー。</param>
    void Complete(string? operationKey = null);

    /// <summary>
    /// エラー発生時など、直近重複を記録せずに送信処理中フラグを解除します。
    /// </summary>
    void Reset();
}

/// <summary>
/// <see cref="IDoubleSubmissionGuard"/> のデフォルト実装。
/// </summary>
public sealed class DoubleSubmissionGuard : IDoubleSubmissionGuard
{
    /// <summary>
    /// 重複操作を検知した際の既定メッセージ。
    /// </summary>
    public const string DuplicateSubmissionMessage = "既にその投稿は送信されました";

    private readonly TimeSpan _recentWindow;
    private int _isSubmitting;
    private string? _currentOperationKey;
    private readonly Dictionary<string, DateTimeOffset> _recentOperations = [];
    private readonly Lock _syncRoot = new();

    public DoubleSubmissionGuard(TimeSpan? recentWindow = null)
    {
        _recentWindow = recentWindow ?? TimeSpan.FromSeconds(5);
    }

    public bool IsSubmitting => Volatile.Read(ref _isSubmitting) == 1;

    public bool TryBeginSubmit(string? operationKey = null)
    {
        lock (_syncRoot)
        {
            if (_isSubmitting == 1)
            {
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            CleanupExpired(now);

            var key = string.IsNullOrWhiteSpace(operationKey) ? null : operationKey.Trim();

            if (!string.IsNullOrEmpty(key) &&
                _recentOperations.TryGetValue(key, out var completedTime) &&
                now - completedTime < _recentWindow)
            {
                return false;
            }

            _currentOperationKey = key;
            _isSubmitting = 1;
            return true;
        }
    }

    public void Complete(string? operationKey = null)
    {
        lock (_syncRoot)
        {
            var key = string.IsNullOrWhiteSpace(operationKey) ? _currentOperationKey : operationKey.Trim();
            var now = DateTimeOffset.UtcNow;
            if (!string.IsNullOrEmpty(_currentOperationKey))
            {
                _recentOperations[_currentOperationKey] = now;
            }
            if (!string.IsNullOrEmpty(key))
            {
                _recentOperations[key] = now;
            }
            CleanupExpired(now);
            _currentOperationKey = null;
            _isSubmitting = 0;
        }
    }

    public void Reset()
    {
        lock (_syncRoot)
        {
            _currentOperationKey = null;
            _isSubmitting = 0;
        }
    }

    /// <summary>
    ///     期限切れとなった重複防止ウィンドウのエントリを辞書から削除する。
    ///     LINQ を用いて宣言的にキーを抽出し、反復中の変更例外を防ぎつつ削除する。
    /// </summary>
    private void CleanupExpired(DateTimeOffset now)
    {
        // 有効期限を過ぎた操作キーを LINQ で抽出
        var expiredKeys = _recentOperations
            .Where(kvp => now - kvp.Value >= _recentWindow)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _recentOperations.Remove(key);
        }
    }
}