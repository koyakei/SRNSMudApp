#region

using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

using MudBlazor.Services;

using SmartComponents.LocalEmbeddings;

using SRNSMudApp.Components;
using SRNSMudApp.Components.Account;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Auth;
using SRNSMudApp.Services.Commands;
using SRNSMudApp.Services.Contracts;
using SRNSMudApp.Services.Dialogs;

#endregion

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

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

// Dialog 起動の抽象化 (単体テスト用モック差し替えポイント)
builder.Services.AddScoped<IDialogLauncher, DialogLauncher>();

// TagCard のデータアクセス分離
builder.Services.AddScoped<ITagCardDataProvider, TagCardDataProvider>();

// ItemCard のデータアクセス分離
builder.Services.AddScoped<IItemCardDataProvider, ItemCardDataProvider>();

// ItemList のデータアクセス分離
builder.Services.AddScoped<IItemListDataProvider, ItemListDataProvider>();

// ItemList の JSON エクスポート構築分離
builder.Services.AddScoped<IItemListExportService, ItemListExportService>();

// TagTree のデータアクセス分離
builder.Services.AddScoped<ITagTreeDataProvider, TagTreeDataProvider>();

// TagTable のデータアクセス分離
builder.Services.AddScoped<ITagTableDataProvider, TagTableDataProvider>();

// Home のデータアクセス分離
builder.Services.AddScoped<IHomeDataProvider, HomeDataProvider>();

// NotificationsPage のデータアクセス分離
builder.Services.AddScoped<INotificationsDataProvider, NotificationsDataProvider>();

// ImportTag のデータアクセス分離
builder.Services.AddScoped<IImportTagDataProvider, ImportTagDataProvider>();

// ItemDetail のデータアクセス分離
builder.Services.AddScoped<IItemDetailDataProvider, ItemDetailDataProvider>();

// TagAddDialog のデータアクセス分離
builder.Services.AddScoped<ITagDialogDataProvider, TagDialogDataProvider>();

// TagDetail のデータアクセス分離
builder.Services.AddScoped<ITagDetailDataProvider, TagDetailDataProvider>();

// Contract 系のデータアクセス分離
builder.Services.AddScoped<IContractDataProvider, ContractDataProvider>();

// User 系のデータアクセス分離
builder.Services.AddScoped<IUserDataProvider, UserDataProvider>();

// 管理・インポート系のデータアクセス分離
builder.Services.AddScoped<IAdminDataProvider, AdminDataProvider>();

// 契約実行 Strategy (IContractExecutor) の登録
builder.Services.AddScoped<IContractExecutor, GratisContractExecutor>();
builder.Services.AddScoped<IContractExecutor, MutualContractExecutor>();
builder.Services.AddScoped<IContractExecutor, TriggerContractExecutor>();
builder.Services.AddScoped<IContractExecutor, BountyContractExecutor>();
builder.Services.AddScoped<IContractExecutorFactory, ContractExecutorFactory>();
builder.Services.AddScoped<TaggingContractService>();
builder.Services.AddScoped<IItemTagService, ItemTagService>();
builder.Services.AddScoped<ITagEdgeService, TagEdgeService>();
builder.Services.AddScoped<ITagDiagramDataProvider, TagDiagramDataProvider>();

// コマンドハンドラー (Command Pattern) の登録
builder.Services.AddScoped<ICommandHandler<ApproveTaggingRequestCommand, Result<string>>, ApproveTaggingRequestHandler>();
builder.Services.AddScoped<ICommandHandler<RejectTaggingRequestCommand, Result<bool>>, RejectTaggingRequestHandler>();

// Register TaggingService
builder.Services.AddTransient<ITaggingService, TaggingService>();
builder.Services.AddScoped<ITaggingRequestActions, TaggingRequestActions>();
builder.Services.AddScoped<ISystemTagEnsurer, SystemTagEnsurer>();

builder.Services.AddScoped<INotificationService, NotificationService>();

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

    _ = builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(connectionString, sqlOptions => sqlOptions.UseHierarchyId()),
        ServiceLifetime.Scoped, // DbContext 自体は今まで通り Scoped (Identity用)
        ServiceLifetime.Singleton); // 設定情報(Options)を Singleton に変更 (Factory用)

    _ = builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
        options.UseSqlServer(connectionString, sqlOptions => sqlOptions.UseHierarchyId()));
}

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// Register LinkPreviewService and HttpClient
builder.Services.AddHttpClient();
builder.Services.AddSingleton<LinkPreviewService>();

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

#pragma warning disable CA1031, RCS1075
    try
    {
        await SeedLock.WaitAsync();
        try
        {
            if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
            {
                try
                {
                    await db.Database.MigrateAsync();
                }
                catch (Exception)
                {
                    // Ignore if already migrated
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
                    EmailConfirmed = true
                };
                _ = await userManager.CreateAsync(systemUser, "SystemPassword123!");
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
        }
        finally
        {
            _ = SeedLock.Release();
        }
    }
    catch (Exception)
    {
        // Ignore in tests due to WebApplicationFactory running this twice
    }
#pragma warning restore CA1031, RCS1075
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