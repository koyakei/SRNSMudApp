#region

using Microsoft.Playwright.NUnit;

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
    public static CustomWebApplicationFactory Factory { get; private set; } = null!;

    public static string ServerAddress => Factory.ServerAddress;

    [OneTimeSetUp]
    public void GlobalSetUp()
    {
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
