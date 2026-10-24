using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components;
using SRNSMudApp.Components.Account;
using SRNSMudApp.Data;
using SRNSMudApp.Extensions;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Auth;
using SRNSMudApp.Services.Providers;
using SRNSMudApp.Services.Push;

namespace SRNSMudApp.Tests.Architecture;

/// <summary>
///     Blazor コンポーネントの [Inject] 依存関係が DI コンテナに正しく登録されているかを検証するアーキテクチャテスト。
///     実行時に DefaultComponentPropertyActivator による InvalidOperationException (未登録サービス例外) が
///     発生するのを防止する。
/// </summary>
public class ComponentInjectedDependenciesTests
{
    /// <summary>
    ///     全ての Blazor コンポーネントにおいて、[Inject] 属性で注入されている ViewModel が
    ///     AddViewModels() メソッドで DI コンテナに登録されていることを検証する。
    /// </summary>
    [Fact]
    public void EveryComponent_InjectedViewModel_MustBeRegisteredInAddViewModels()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new Mock<IUserDataProvider>().Object);
        services.AddViewModels();

        var registeredTypes = services.Select(d => d.ServiceType).ToHashSet();
        var componentTypes = GetComponentTypes();
        var missingRegistrations = new List<string>();

        // Act
        foreach (var componentType in componentTypes)
        {
            var injectedProperties = GetInjectedProperties(componentType);
            foreach (var property in injectedProperties)
            {
                var propType = property.PropertyType;
                if (propType.Name.EndsWith("ViewModel", StringComparison.Ordinal) ||
                    (propType.Namespace != null && propType.Namespace.Contains("ViewModel", StringComparison.Ordinal)))
                {
                    if (!registeredTypes.Contains(propType))
                    {
                        missingRegistrations.Add(
                            $"Cannot provide a value for property '{property.Name}' on type '{componentType.FullName}'. " +
                            $"There is no registered service of type '{propType.FullName}'.");
                    }
                }
            }
        }

        // Assert
        Assert.True(
            missingRegistrations.Count == 0,
            $"Found {missingRegistrations.Count} unregistered ViewModel injection(s):{Environment.NewLine}" +
            string.Join(Environment.NewLine, missingRegistrations));
    }

    /// <summary>
    ///     全ての Blazor コンポーネントにおいて、[Inject] 属性で注入されている全ての依存サービスが
    ///     DI コンテナから null でなく解決できることを検証する。
    /// </summary>
    [Fact]
    public void EveryComponent_AllInjectedProperties_CanBeResolvedFromServiceProvider()
    {
        // Arrange
        using var provider = CreateServiceProviderForComponents();
        using var scope = provider.CreateScope();
        var componentTypes = GetComponentTypes();
        var failures = new List<string>();

        // Act
        foreach (var componentType in componentTypes)
        {
            var injectedProperties = GetInjectedProperties(componentType);
            foreach (var property in injectedProperties)
            {
                try
                {
                    var service = scope.ServiceProvider.GetService(property.PropertyType);
                    if (service is null)
                    {
                        failures.Add(
                            $"Cannot provide a value for property '{property.Name}' on type '{componentType.FullName}'. " +
                            $"There is no registered service of type '{property.PropertyType.FullName}'.");
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(
                        $"Failed to activate property '{property.Name}' on type '{componentType.FullName}' " +
                        $"for service '{property.PropertyType.FullName}': {ex.Message}");
                }
            }
        }

        // Assert
        Assert.True(
            failures.Count == 0,
            $"Found {failures.Count} unresolvable component dependency injection(s):{Environment.NewLine}" +
            string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    ///     AddViewModels() に登録されている全ての ViewModel が
    ///     DI コンテナから正常にインスタンス化（依存解決）できることを検証する。
    /// </summary>
    [Fact]
    public void AllRegisteredViewModels_CanBeResolvedFromServiceProvider()
    {
        // Arrange
        using var provider = CreateServiceProviderForComponents();
        using var scope = provider.CreateScope();

        var services = new ServiceCollection();
        services.AddViewModels();

        var failures = new List<string>();

        // Act
        foreach (var descriptor in services)
        {
            try
            {
                var instance = scope.ServiceProvider.GetService(descriptor.ServiceType);
                if (instance is null)
                {
                    failures.Add($"ViewModel '{descriptor.ServiceType.FullName}' could not be resolved (returned null).");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"ViewModel '{descriptor.ServiceType.FullName}' failed to resolve: {ex.Message}");
            }
        }

        // Assert
        Assert.True(
            failures.Count == 0,
            $"Found {failures.Count} ViewModel resolution failure(s):{Environment.NewLine}" +
            string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    ///     全ての Blazor コンポーネントをインスタンス化し、[Inject] プロパティに解決したサービスを
    ///     設定（DefaultComponentPropertyActivator と同等のプロパティ注入）できることを検証する。
    /// </summary>
    [Fact]
    public void EveryComponent_CanBeActivatedAndInjected()
    {
        // Arrange
        using var provider = CreateServiceProviderForComponents();
        using var scope = provider.CreateScope();
        var componentTypes = GetComponentTypes();
        var failures = new List<string>();

        // Act
        foreach (var componentType in componentTypes)
        {
            try
            {
                Type targetType = componentType;
                if (targetType.ContainsGenericParameters)
                {
                    var typeArgs = targetType.GetGenericArguments().Select(arg =>
                    {
                        var constraints = arg.GetGenericParameterConstraints();
                        if (constraints.Length == 0)
                        {
                            return typeof(object);
                        }

                        if (constraints.Any(c => c.IsAssignableFrom(typeof(Item))))
                        {
                            return typeof(Item);
                        }

                        return constraints[0];
                    }).ToArray();
                    targetType = targetType.MakeGenericType(typeArgs);
                }

                var instance = Activator.CreateInstance(targetType);
                if (instance is null)
                {
                    failures.Add($"Failed to instantiate component '{componentType.FullName}'.");
                    continue;
                }

                var injectedProperties = GetInjectedProperties(targetType);
                foreach (var property in injectedProperties)
                {
                    var service = scope.ServiceProvider.GetService(property.PropertyType);
                    if (service is null)
                    {
                        failures.Add(
                            $"Cannot provide a value for property '{property.Name}' on type '{componentType.FullName}'. " +
                            $"There is no registered service of type '{property.PropertyType.FullName}'.");
                    }
                    else if (property.CanWrite)
                    {
                        property.SetValue(instance, service);
                    }
                }
            }
            catch (Exception ex)
            {
                failures.Add($"Failed to activate component '{componentType.FullName}': {ex.Message}");
            }
        }

        // Assert
        Assert.True(
            failures.Count == 0,
            $"Found {failures.Count} component activation failure(s):{Environment.NewLine}" +
            string.Join(Environment.NewLine, failures));
    }

    private static Type[] GetComponentTypes()
    {
        var assemblies = new[]
        {
            typeof(App).Assembly,
            typeof(SRNSMudApp.Client._Imports).Assembly
        };

        return assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IComponent).IsAssignableFrom(t))
            .Distinct()
            .ToArray();
    }

    private static IEnumerable<PropertyInfo> GetInjectedProperties(Type componentType)
    {
        var current = componentType;
        var seen = new HashSet<string>();
        while (current != null && current != typeof(object))
        {
            var properties = current.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            foreach (var property in properties)
            {
                if (property.GetCustomAttributes(typeof(InjectAttribute), inherit: true).Length != 0)
                {
                    if (seen.Add($"{property.DeclaringType?.FullName}.{property.Name}"))
                    {
                        yield return property;
                    }
                }
            }
            current = current.BaseType;
        }
    }

    private static ServiceProvider CreateServiceProviderForComponents()
    {
        var services = new ServiceCollection();

        // Logging & Localization
        services.AddLogging();
        services.AddLocalization();

        // MudBlazor
        services.AddMudServices();

        // HTTP Client
        services.AddHttpClient();

        // Configuration & Environment
        services.AddSingleton<IConfiguration>(new ConfigurationManager());
        services.AddSingleton(Mock.Of<IWebHostEnvironment>());

        // DB 関連 (モック IDbContextFactory)
        var mockDbFactory = new Mock<Microsoft.EntityFrameworkCore.IDbContextFactory<ApplicationDbContext>>();
        services.AddSingleton(mockDbFactory.Object);
        services.AddSingleton(new ApplicationDbContext(new Microsoft.EntityFrameworkCore.DbContextOptions<ApplicationDbContext>()));

        // アプリケーション本体のサービス登録群 (Program.cs と共通)
        services.AddDataProviders();
        services.AddContractAndCommandServices();
        services.AddTaggingAndDomainServices();
        services.AddViewModels();

        // Program.cs で直接登録されているサービス群
        services.AddScoped<IExternalTokenVerificationService, ExternalTokenVerificationService>();
        services.AddScoped<IRiskAssessmentService, RiskAssessmentService>();
        services.AddScoped(sp => (RiskAssessmentService)sp.GetRequiredService<IRiskAssessmentService>());
        services.AddSingleton<IPushSubscriptionStore, InMemoryPushSubscriptionStore>();
        services.AddScoped<IWebPushNotificationService, FirebaseWebPushService>();
        services.AddSingleton<ILinkPreviewProvider, ItemLinkPreviewProvider>();
        services.AddSingleton<ILinkPreviewProvider, TagLinkPreviewProvider>();
        services.AddSingleton<ILinkPreviewProvider, UserLinkPreviewProvider>();
        services.AddSingleton<ILinkPreviewProvider, ExternalOgpLinkPreviewProvider>();
        services.AddSingleton<LinkPreviewService>();
        services.AddSingleton<ILinkPreviewService>(sp => sp.GetRequiredService<LinkPreviewService>());
        services.AddSingleton<ITagEmbeddingService>(new Mock<ITagEmbeddingService>().Object);
        services.AddScoped<SRNSMudApp.Client.Services.IItemListApiClient>(sp => new Mock<SRNSMudApp.Client.Services.IItemListApiClient>().Object);
        services.AddScoped<SRNSMudApp.Client.Services.ITagApiClient>(sp => sp.GetRequiredService<SRNSMudApp.Client.Services.IItemListApiClient>());
        services.AddScoped<SRNSMudApp.Client.Services.ITagProposalApiClient>(sp => sp.GetRequiredService<SRNSMudApp.Client.Services.IItemListApiClient>());
        services.AddScoped<SRNSMudApp.Client.Services.ITagLedgerApiClient>(sp => sp.GetRequiredService<SRNSMudApp.Client.Services.IItemListApiClient>());
        services.AddScoped<SRNSMudApp.Client.Services.ITaggingRequestApiClient>(sp => sp.GetRequiredService<SRNSMudApp.Client.Services.IItemListApiClient>());

        // JS Runtime & NavigationManager
        services.AddSingleton(Mock.Of<IJSRuntime>());
        services.AddSingleton<NavigationManager>(new TestNavigationManager());

        // Authentication & Authorization
        var authState = new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "test-user")], "TestAuth")));
        var authMock = new Mock<AuthenticationStateProvider>();
        authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        services.AddScoped(_ => authMock.Object);
        services.AddAuthorizationCore();
        services.AddCascadingAuthenticationState();

        // Identity
        var userStoreMock = new Mock<IUserStore<ApplicationUser>>();
        services.AddScoped(_ => userStoreMock.Object);
        var roleStoreMock = new Mock<IRoleStore<IdentityRole>>();
        services.AddScoped(_ => roleStoreMock.Object);
        services.AddScoped(_ =>
        {
            var roleMgr = new Mock<RoleManager<IdentityRole>>(roleStoreMock.Object, null!, null!, null!, null!);
            return roleMgr.Object;
        });
        services.AddScoped(_ =>
        {
            var mgr = new Mock<UserManager<ApplicationUser>>(userStoreMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);
            return mgr.Object;
        });
        services.AddScoped(_ =>
        {
            var userMgr = new Mock<UserManager<ApplicationUser>>(userStoreMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);
            var contextAccessor = new Mock<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
            var claimsFactory = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
            return new Mock<SignInManager<ApplicationUser>>(userMgr.Object, contextAccessor.Object, claimsFactory.Object, null!, null!, null!, null!).Object;
        });
        services.AddScoped<IdentityRedirectManager>();
        services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

        // Antiforgery
        var antiforgeryMock = new Mock<AntiforgeryStateProvider>();
        services.AddScoped(_ => antiforgeryMock.Object);

        // Utilities
        services.AddSingleton(UrlEncoder.Default);

        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager()
        {
            Initialize("http://localhost/", "http://localhost/");
        }

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Uri = ToAbsoluteUri(uri).ToString();
        }
    }
}