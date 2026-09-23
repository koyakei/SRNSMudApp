#region

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.Services.Auth;

/// <summary>
///     最初に登録された一般ユーザー（system を除く）を Admin ロールに自動昇格する実装。
///     初回デプロイ後の最初の登録ユーザーをサービス管理者として扱う用途を想定している。
/// </summary>
public sealed partial class FirstUserAdminService(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    ILogger<FirstUserAdminService> logger) : IFirstUserAdminService
{
    private readonly UserManager<ApplicationUser> _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
    private readonly RoleManager<IdentityRole> _roleManager = roleManager ?? throw new ArgumentNullException(nameof(roleManager));
    private readonly ILogger<FirstUserAdminService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning, Message = "FirstUserAdminService: userId={UserId} のユーザーが見つかりません。Admin 昇格をスキップします。")]
    private static partial void LogUserNotFound(ILogger logger, string userId);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Information, Message = "FirstUserAdminService: userId={UserId} を Admin ロールに昇格しました（初回登録ユーザー）。")]
    private static partial void LogPromotedToAdmin(ILogger logger, string userId);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Warning, Message = "FirstUserAdminService: userId={UserId} の Admin 昇格に失敗しました: {Errors}")]
    private static partial void LogPromotionFailed(ILogger logger, string userId, string errors);

    /// <inheritdoc />
    public async Task GrantAdminIfFirstUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        // system ユーザー以外の一般ユーザーが既に 1 人でも存在する場合は昇格しない（べき等保証）
        bool hasOtherUser = await _userManager.Users
            .AnyAsync(u => u.Id != "system" && u.Id != userId, cancellationToken);

        if (hasOtherUser)
        {
            return;
        }

        ApplicationUser? user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            LogUserNotFound(_logger, userId);
            return;
        }

        // Admin ロールが存在しない場合は作成（シード済みが前提だが念のため保証）
        if (!await _roleManager.RoleExistsAsync("Admin"))
        {
            _ = await _roleManager.CreateAsync(new IdentityRole("Admin"));
        }

        if (!await _userManager.IsInRoleAsync(user, "Admin"))
        {
            IdentityResult result = await _userManager.AddToRoleAsync(user, "Admin");
            if (result.Succeeded)
            {
                LogPromotedToAdmin(_logger, userId);
            }
            else
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                LogPromotionFailed(_logger, userId, errors);
            }
        }
    }
}