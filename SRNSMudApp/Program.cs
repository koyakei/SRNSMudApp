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
using SRNSMudApp.Services;
using SRNSMudApp.Services.Auth;

using _Imports = SRNSMudApp.Client._Imports;

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

builder.Services.AddScoped<TaggingContractService>();
builder.Services.AddScoped<IItemTagService, ItemTagService>();

// Register TaggingService
builder.Services.AddTransient<ITaggingService, TaggingService>();

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

    _ = builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(connectionString),
        ServiceLifetime.Scoped, // DbContext 自体は今まで通り Scoped (Identity用)
        ServiceLifetime.Singleton); // 設定情報(Options)を Singleton に変更 (Factory用)

    _ = builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
        options.UseSqlServer(connectionString));
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

// Seed Admin Role
using (IServiceScope scope = app.Services.CreateScope())
{
    if (app.Environment.IsEnvironment("Testing"))
    {
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
#pragma warning disable CA1031, RCS1075
        try
        {
            _ = await db.Database.EnsureCreatedAsync();
        }
        catch (Exception)
        {
            // WebApplicationFactory runs Program.cs multiple times. Ignore if already created.
        }
#pragma warning restore CA1031, RCS1075
    }

    RoleManager<IdentityRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
#pragma warning disable CA1031, RCS1075
    try
    {
        await SeedLock.WaitAsync();
        try
        {
            if (!await roleManager.RoleExistsAsync("Admin"))
            {
                _ = await roleManager.CreateAsync(new IdentityRole("Admin"));
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

app.Use(async (context, next) =>
{
    Endpoint? endpoint = context.GetEndpoint();
    await File.AppendAllTextAsync("/tmp/routing.log",
        $"[ROUTING] Path: {context.Request.Path}, Matched Endpoint: {endpoint?.DisplayName ?? "NULL"}\n");
    await next(context);
});

app.MapControllers();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(_Imports).Assembly);

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

await app.RunAsync();

#pragma warning disable CA1052
public partial class Program
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
}
#pragma warning restore CA1052