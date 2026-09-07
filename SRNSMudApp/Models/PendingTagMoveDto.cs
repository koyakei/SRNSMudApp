namespace SRNSMudApp.Models;

/// <summary>
///     移動申請中（承認待ち）のタグ情報をツリー表示用に保持する DTO。
/// </summary>
/// <param name="RequestId">タグ移動リクエスト（TaggingRequestEntity）の ID。</param>
/// <param name="TagId">移動対象のタグ ID。</param>
/// <param name="TagName">移動対象のタグ名。</param>
/// <param name="NewParentTagId">リクエストされた新しい親タグ ID（null の場合はルート直下）。</param>
/// <param name="RequesterUserId">移動リクエストを送信したユーザー ID。</param>
/// <param name="TagOwnerUserId">移動対象タグの所有者ユーザー ID。</param>
public record PendingTagMoveDto(
    int RequestId,
    int TagId,
    string TagName,
    int? NewParentTagId,
    string RequesterUserId,
    string TagOwnerUserId);