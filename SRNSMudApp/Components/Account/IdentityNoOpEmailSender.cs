#region

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.Components.Account;

/// <summary>
///     本番用のメール送信サービスが設定されるまでのノーオペレーション用メール送信実装。
///     開発環境やテスト環境でメール送信をモックするために使用される。
/// </summary>
internal sealed class IdentityNoOpEmailSender : IEmailSender<ApplicationUser>
{
    private readonly NoOpEmailSender _emailSender = new();

    /// <summary>
    ///     アカウント確認リンクをメールで送信する。
    /// </summary>
    /// <param name="user">対象の <see cref="ApplicationUser" />。</param>
    /// <param name="email">送信先メールアドレス。</param>
    /// <param name="confirmationLink">確認用リンクURL。</param>
    /// <returns>送信処理を表すタスク。</returns>
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmationLink);

        return _emailSender.SendEmailAsync(
            email,
            "Confirm your email",
            $"Please confirm your account by <a href='{confirmationLink}'>clicking here</a>.");
    }

    /// <summary>
    ///     パスワードリセットリンクをメールで送信する。
    /// </summary>
    /// <param name="user">対象の <see cref="ApplicationUser" />。</param>
    /// <param name="email">送信先メールアドレス。</param>
    /// <param name="resetLink">リセット用リンクURL。</param>
    /// <returns>送信処理を表すタスク。</returns>
    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(resetLink);

        return _emailSender.SendEmailAsync(
            email,
            "Reset your password",
            $"Please reset your password by <a href='{resetLink}'>clicking here</a>.");
    }

    /// <summary>
    ///     パスワードリセット確認コードをメールで送信する。
    /// </summary>
    /// <param name="user">対象の <see cref="ApplicationUser" />。</param>
    /// <param name="email">送信先メールアドレス。</param>
    /// <param name="resetCode">リセット確認コード。</param>
    /// <returns>送信処理を表すタスク。</returns>
    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(resetCode);

        return _emailSender.SendEmailAsync(
            email,
            "Reset your password",
            $"Please reset your password using the following code: {resetCode}");
    }
}