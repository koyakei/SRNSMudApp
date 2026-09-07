namespace SRNSMudApp.Models;

/// <summary>
///     リプライ時の通知対象候補ユーザー（親アイテムオーナーや既存リプライヤー）。
/// </summary>
public record ReplyTargetCandidate(string Id, string UserName);