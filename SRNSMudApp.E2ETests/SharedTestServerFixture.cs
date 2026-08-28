#region

#endregion

namespace SRNSMudApp.E2ETests;

/// <summary>
///     テストアセンブリ全体で1つの CustomWebApplicationFactory（＝1つのMSSQL Testcontainersコンテナ）
///     を共有するための NUnit フィクスチャ。
///     これにより各テストクラスの OneTimeSetUp ごとのコンテナ起動コストが排除される。
///
///     注意:
///     - NUnit の並列化は有効化されていないため、テストクラスは逐次実行される。
///       共有DB上でのデータ干渉を避けるため、各テストは Guid ベースのユニークな
///       メールアドレス/ユーザー名を使うこと（既存テストはその方針で統一済み）。
///     - ExternalLoginButtonsE2ETests が必要とするダミーの Google ClientId は
///       ホスト構築前にここで設定する（ホストは1回しか構築されないため）。
/// </summary>
[SetUpFixture]
public class SharedTestServerFixture
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("NUnit", "NUnit1028:Non-test methods in SetUpFixture", Justification = "ModuleInitializer is required to be internal for module level access")]
    internal static void ModuleInit()
    {
        SanitizeBrowserEnvironment();
    }

    private static void SanitizeBrowserEnvironment()
    {
        var browser = Environment.GetEnvironmentVariable("BROWSER");
        if (string.IsNullOrWhiteSpace(browser))
        {
            return;
        }

        // Playwright.NUnit が解釈可能なブラウザ名は 'chromium', 'firefox', 'webkit' のみ。
        // VS Code / Dev Container / Linux 環境等で BROWSER に実行ファイルやシェルスクリプトのパスが
        // 自動設定されている場合、PlaywrightSettingsProvider が ArgumentException をスローするためクリアする。
        if (!string.Equals(browser, "chromium", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(browser, "firefox", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(browser, "webkit", StringComparison.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("BROWSER", null);
        }
    }

    public static CustomWebApplicationFactory Factory { get; private set; } = null!;

    public static string ServerAddress => Factory.ServerAddress;

    [OneTimeSetUp]
    public void GlobalSetUp()
    {
        SanitizeBrowserEnvironment();

        Environment.SetEnvironmentVariable("Authentication__Google__ClientId",
            "1234567890-dummy.apps.googleusercontent.com");

        Factory = new CustomWebApplicationFactory();
        Factory.EnsureServer();

        Console.WriteLine(
            $"[SharedTestServerFixture] MSSQL container + Kestrel started once. Address: {Factory.ServerAddress}");
    }

    [OneTimeTearDown]
    public void GlobalTearDown()
    {
        Factory?.Dispose();
        Environment.SetEnvironmentVariable("Authentication__Google__ClientId", null);
    }
}