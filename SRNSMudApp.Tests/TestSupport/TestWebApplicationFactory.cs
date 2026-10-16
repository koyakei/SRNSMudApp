#region

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.Tests.TestSupport;

/// <summary>
///     SRNSMudApp.Tests 全体で共有可能な統合テスト用 WebApplicationFactory。
///     "Testing" 環境を明示的に指定し、SharedMsSqlTestDatabase の接続先を注入することで、
///     余分なマイグレーション再試行ループや接続タイムアウトを防ぎ、高速なホスト初期化を提供する。
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var db = SharedMsSqlTestDatabase.GetInstanceAsync().GetAwaiter().GetResult();
        _ = builder.UseSetting("ConnectionStrings:DefaultConnection", db.ConnectionString);
        _ = builder.UseEnvironment("Testing");
        _ = builder.ConfigureServices(services =>
        {

            _ = services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            _ = services.RemoveAll<DbContextOptions>();
            _ = services.RemoveAll<IDbContextFactory<ApplicationDbContext>>();
            _ = services.RemoveAll<ApplicationDbContext>();

            _ = services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(db.ConnectionString, sqlOptions => sqlOptions.UseHierarchyId()),
                ServiceLifetime.Scoped, ServiceLifetime.Singleton);

            _ = services.AddDbContextFactory<ApplicationDbContext>(options =>
                options.UseSqlServer(db.ConnectionString, sqlOptions => sqlOptions.UseHierarchyId()));
        });
    }
}