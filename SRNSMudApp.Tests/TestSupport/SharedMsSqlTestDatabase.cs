using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

using Testcontainers.MsSql;

namespace SRNSMudApp.Tests.TestSupport;

/// <summary>
/// テストアセンブリ全体で共有される単一の MSSQL Testcontainers コンテナおよびマイグレーション済みデータベース。
/// 全テストクラスがこの共有インスタンスに対して並行で読み書きを行い、tid（名前空間）によってデータを完全分離する。
/// </summary>
public static class SharedMsSqlTestDatabase
{
    private static readonly SemaphoreSlim InitLock = new(1, 1);
    private static MsSqlContainer? s_container;
    private static MsSqlTestDatabase? s_database;
    private static bool s_isInitialized;

    public static async Task<MsSqlTestDatabase> GetInstanceAsync()
    {
        if (s_isInitialized && s_database != null)
        {
            return s_database;
        }

        await InitLock.WaitAsync();
        try
        {
            if (!s_isInitialized || s_database == null)
            {
                s_container = new MsSqlBuilder().Build();
                await s_container.StartAsync();

                s_database = await MsSqlTestDatabase.CreateAsync(s_container.GetConnectionString(), "SharedTestDb");
                s_isInitialized = true;
            }

            return s_database;
        }
        finally
        {
            _ = InitLock.Release();
        }
    }

    public static async Task<ApplicationDbContext> CreateDbContextAsync()
    {
        var db = await GetInstanceAsync();
        return new ApplicationDbContext(db.Options);
    }
}