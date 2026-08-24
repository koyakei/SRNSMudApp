using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SRNSMudApp.Data;

namespace SRNSMudApp.Tests.TestSupport;

/// <summary>
///     テストごとに一意な名前の DB を作成・マイグレーション適用し、終了時に DROP するヘルパー。
/// </summary>
public sealed class MsSqlTestDatabase : IAsyncDisposable
{
    private readonly string _baseConnectionString;
    private readonly string _databaseName;

    public DbContextOptions<ApplicationDbContext> Options { get; }
    public string ConnectionString { get; }

    private MsSqlTestDatabase(
        string baseConnectionString,
        string databaseName,
        string dbConnectionString,
        DbContextOptions<ApplicationDbContext> options)
    {
        _baseConnectionString = baseConnectionString;
        _databaseName = databaseName;
        ConnectionString = dbConnectionString;
        Options = options;
    }

    public static async Task<MsSqlTestDatabase> CreateAsync(string containerConnectionString, string? namePrefix = null)
    {
        var databaseName = $"{(namePrefix ?? "test").Replace(" ", "_")}_{Guid.NewGuid():N}";
        var builder = new SqlConnectionStringBuilder(containerConnectionString)
        {
            InitialCatalog = databaseName,
            MultipleActiveResultSets = true
        };
        var dbConnectionString = builder.ConnectionString;

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(dbConnectionString)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        await using (var context = new ApplicationDbContext(options))
        {
            await context.Database.MigrateAsync();
        }

        return new MsSqlTestDatabase(containerConnectionString, databaseName, dbConnectionString, options);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var connection = new SqlConnection(_baseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                                 $"DROP DATABASE [{_databaseName}];";
            await command.ExecuteNonQueryAsync();
        }
        catch
        {
            // 後始末の失敗はテスト結果に影響させない。コンテナ自体はアセンブリ終了時に破棄される。
        }
    }
}
