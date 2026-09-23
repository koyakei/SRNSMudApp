#region

using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

using MudBlazor.Services;

using SmartComponents.LocalEmbeddings;

using SRNSMudApp.Components;
using SRNSMudApp.Components.Account;
using SRNSMudApp.Data;
using SRNSMudApp.Extensions;
using SRNSMudApp.Middlewares;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Auth;
using SRNSMudApp.Services.Providers;

#endregion

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Add Data Protection configuration
// devcontainer やコンテナ環境でキーが消失して Antiforgery / 認証トークンの復号例外 (CryptographicException) が
// 発生するのを防ぐため、キーの永続化先ディレクトリを設定する。
IDataProtectionBuilder dataProtectionBuilder = builder.Services.AddDataProtection()
    .SetApplicationName("SRNSMudApp");

if (!builder.Environment.IsEnvironment("Testing"))
{
    string? keysPath = builder.Configuration["DataProtection:KeyPath"];
    if (string.IsNullOrWhiteSpace(keysPath) && builder.Environment.IsDevelopment())
    {
        // 開発環境のデフォルト: ワークスペース内の .aspnet/DataProtection-Keys に永続化
        // ワークスペースはホストとマウントされているため、devcontainer の再作成でもキーが失われない
        keysPath = Path.Combine(builder.Environment.ContentRootPath, ".aspnet", "DataProtection-Keys");
    }

    if (!string.IsNullOrWhiteSpace(keysPath))
    {
        _ = Directory.CreateDirectory(keysPath);
        _ = dataProtectionBuilder.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
    }
}

// Add Auth services
builder.Services.AddScoped<IExternalTokenVerificationService, ExternalTokenVerificationService>();
builder.Services.AddScoped<RiskAssessmentService>();

// Add controllers for API endpoints
builder.Services.AddControllers();

// Add Rate Limiting for Auth
builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter("AuthRateLimit", limiterOptions =>
{
    limiterOptions.PermitLimit = builder.Environment.IsEnvironment("Testing") ? 1000 : 5;
    limiterOptions.Window = TimeSpan.FromMinutes(1);
    limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    limiterOptions.QueueLimit = 2;
}));

// Add MudBlazor services
builder.Services.AddMudServices();

// Add Localization for Resource Pattern
builder.Services.AddLocalization();

// UI DataProvider 群の登録 (Provider Pattern による UI/DbContext 分離)
builder.Services.AddDataProviders();

// 契約 Strategy / Factory およびコマンドハンドラーの登録 (Strategy, Factory, Command Pattern)
builder.Services.AddContractAndCommandServices();

// ドメインサービス・ダイアログ抽象化の登録
builder.Services.AddTaggingAndDomainServices();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

// テスト環境の場合は CustomWebApplicationFactory 側で DB を登録するためスキップする
if (!builder.Environment.IsEnvironment("Testing"))
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ??
                           throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

    const string fallbackPassword = "ChangeMe!Passw0rd";
    var configuredPassword = Environment.GetEnvironmentVariable("MSSQL_SA_PASSWORD");
    if (!string.IsNullOrWhiteSpace(configuredPassword))
    {
        connectionString = connectionString.Replace(fallbackPassword, configuredPassword, StringComparison.Ordinal);
    }
    else if (builder.Environment.IsProduction() && connectionString.Contains(fallbackPassword, StringComparison.Ordinal))
    {
        throw new InvalidOperationException("本番環境では環境変数 'MSSQL_SA_PASSWORD' の設定が必須です。デフォルトのフォールバックパスワードは使用できません。");
    }

    _ = builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.UseHierarchyId();
                sqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: null);
            }),
        ServiceLifetime.Scoped, // DbContext 自体は今まで通り Scoped (Identity用)
        ServiceLifetime.Singleton); // 設定情報(Options)を Singleton に変更 (Factory用)

    _ = builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
        options.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.UseHierarchyId();
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
        }));
}

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        // 任意の文字列（日本語等）をユーザー名として許可するため、文字種制限を解除
        options.User.AllowedUserNameCharacters = null;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// Register LinkPreview providers and services (Strategy Pattern)
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ILinkPreviewProvider, ItemLinkPreviewProvider>();
builder.Services.AddSingleton<ILinkPreviewProvider, TagLinkPreviewProvider>();
builder.Services.AddSingleton<ILinkPreviewProvider, UserLinkPreviewProvider>();
builder.Services.AddSingleton<ILinkPreviewProvider, ExternalOgpLinkPreviewProvider>();
builder.Services.AddSingleton<LinkPreviewService>();
builder.Services.AddSingleton<ILinkPreviewService>(sp => sp.GetRequiredService<LinkPreviewService>());

// Register SmartComponents.LocalEmbeddings and TagEmbeddingService
builder.Services.AddSingleton<LocalEmbedder>();
builder.Services.AddSingleton<ITagEmbeddingService, TagEmbeddingService>();

WebApplication app = builder.Build();

// Seed Admin Role and Root Tag
using (IServiceScope scope = app.Services.CreateScope())
{
    ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    RoleManager<IdentityRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

#pragma warning disable CA1031, CA1848, RCS1075
    try
    {
        await SeedLock.WaitAsync();
        try
        {
            if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing") || string.Equals(Environment.GetEnvironmentVariable("AUTO_MIGRATE"), "true", StringComparison.OrdinalIgnoreCase))
            {
                const int maxRetries = 3;
                for (var attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        await db.Database.MigrateAsync();
                        break;
                    }
                    catch (Exception ex) when (attempt < maxRetries)
                    {
                        Console.WriteLine($"[WARNING] db.Database.MigrateAsync attempt {attempt} failed: {ex.Message}. Retrying in 5 seconds...");
                        await Task.Delay(5000);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] db.Database.MigrateAsync failed after {maxRetries} attempts: {ex}");
                        throw;
                    }
                }
            }

            if (!await roleManager.RoleExistsAsync("Admin"))
            {
                _ = await roleManager.CreateAsync(new IdentityRole("Admin"));
            }

            ApplicationUser? systemUser = await userManager.FindByNameAsync("system");
            if (systemUser is null)
            {
                systemUser = new ApplicationUser
                {
                    Id = "system",
                    UserName = "system",
                    Email = "system@example.com",
                    EmailConfirmed = true,
                    LockoutEnabled = true,
                    LockoutEnd = DateTimeOffset.MaxValue
                };
                var systemPassword = Environment.GetEnvironmentVariable("SYSTEM_USER_INITIAL_PASSWORD")
                    ?? builder.Configuration["SystemUser:InitialPassword"];

                if (string.IsNullOrWhiteSpace(systemPassword))
                {
                    // system ユーザーはシステム内部用アカウント（タグ所有等）のため、未指定時は安全なランダムパスワードを自動生成
                    systemPassword = $"Sys_{Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))}!aA1";
                }

                _ = await userManager.CreateAsync(systemUser, systemPassword);
                _ = await userManager.SetLockoutEnabledAsync(systemUser, true);
                _ = await userManager.SetLockoutEndDateAsync(systemUser, DateTimeOffset.MaxValue);
            }
            else if (!systemUser.LockoutEnabled || systemUser.LockoutEnd != DateTimeOffset.MaxValue)
            {
                // 既存の system ユーザーが存在する場合も確実にロックアウトしてログイン不可にする
                _ = await userManager.SetLockoutEnabledAsync(systemUser, true);
                _ = await userManager.SetLockoutEndDateAsync(systemUser, DateTimeOffset.MaxValue);
            }

            if (!await db.Tags.AnyAsync(t => t.Name == Tag.RootTagName))
            {
                var rootTag = new Tag
                {
                    Name = Tag.RootTagName,
                    Content = "全てのタグの頂点となるルートタグ",
                    IsSystem = true,
                    OwnerId = systemUser.Id,
                    Node = HierarchyId.GetRoot(),
                    ParentTagId = null,
                    CreatedDate = DateTime.UtcNow,
                    UpdatedDate = DateTime.UtcNow
                };
                _ = db.Tags.Add(rootTag);
                _ = await db.SaveChangesAsync();
            }

            // 公式システム分類タグツリーのシード（未投入の場合に自動投入）
            _ = await SystemTagSeedService.SeedSystemTagsAsync(db, systemUser.Id, app.Logger);
        }
        finally
        {
            _ = SeedLock.Release();
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "DBシード処理中にエラーが発生しました。");
    }
#pragma warning restore CA1031, CA1848, RCS1075
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    _ = app.UseMigrationsEndPoint();
    _ = app.UseDeveloperExceptionPage();
}
else
{
    _ = app.UseExceptionHandler("/Error", true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    _ = app.UseHsts();
}

// 復号不可能な古い Antiforgery Cookie を検知・クリーンアップし、CryptographicException ログエラーを防止
// 下流のルーティング・ステータスページ・Blazor コンポーネントより前で確実に古い Cookie を除去する
app.UseAntiforgeryCookieCleanup();

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseHttpsRedirection();

app.UseRateLimiter();

app.UseAntiforgery();

app.MapStaticAssets();



app.MapControllers();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

await app.RunAsync();

#pragma warning disable CA1052
public partial class Program
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
}
#pragma warning restore CA1052