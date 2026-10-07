namespace SRNSMudApp.Models;

using System;

using SRNSMudApp.Data;

/// <summary>
///     ユーザープロファイルの公開属性のみを安全にカプセル化する DTO。
///     IdentityUser のセンシティブ情報（PasswordHash, SecurityStamp, ConcurrencyStamp 等）の漏洩を防止する。
/// </summary>
public sealed record UserProfileDto(
    string Id,
    string? UserName,
    string? Email,
    bool IsBanned,
    DateTimeOffset? BannedAt,
    string? BanReason,
    bool IsPrivateModeDefault,
    float? TagSuggestionStrongThreshold,
    float? TagSuggestionCandidateThreshold,
    bool IsLinkConversionEnabled,
    float? LinkConversionThreshold)
{
    /// <summary>
    ///     <see cref="ApplicationUser" /> エンティティから公開用 <see cref="UserProfileDto" /> へ変換する。
    /// </summary>
    public static UserProfileDto? FromEntity(ApplicationUser? user)
    {
        if (user is null)
        {
            return null;
        }

        return new UserProfileDto(
            user.Id,
            user.UserName,
            user.Email,
            user.IsBanned,
            user.BannedAt,
            user.BanReason,
            user.IsPrivateModeDefault,
            user.TagSuggestionStrongThreshold,
            user.TagSuggestionCandidateThreshold,
            user.IsLinkConversionEnabled,
            user.LinkConversionThreshold);
    }
}