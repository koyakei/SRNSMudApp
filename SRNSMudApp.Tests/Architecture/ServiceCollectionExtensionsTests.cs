using Microsoft.Extensions.DependencyInjection;

using SRNSMudApp.Extensions;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Commands;
using SRNSMudApp.Services.Contracts;
using SRNSMudApp.Services.Dialogs;

namespace SRNSMudApp.Tests.Architecture;

/// <summary>
///     ServiceCollectionExtensions の DI 登録メソッドが期待通りにサービスを登録することを検証するテスト。
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddDataProviders_RegistersExpectedProviders()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddDataProviders();

        // Assert: 主要な DataProvider が登録されていることを検証
        Assert.Contains(services, d => d.ServiceType == typeof(ITagCardDataProvider) && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, d => d.ServiceType == typeof(IItemCardDataProvider) && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, d => d.ServiceType == typeof(IItemListDataProvider) && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, d => d.ServiceType == typeof(ITagTreeDataProvider) && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, d => d.ServiceType == typeof(ITagTableDataProvider) && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, d => d.ServiceType == typeof(IHomeDataProvider) && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, d => d.ServiceType == typeof(INotificationsDataProvider) && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, d => d.ServiceType == typeof(IItemDetailDataProvider) && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, d => d.ServiceType == typeof(ITagDetailDataProvider) && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, d => d.ServiceType == typeof(ITagDiagramDataProvider) && d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddContractAndCommandServices_RegistersExpectedServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddContractAndCommandServices();

        // Assert: 契約 Strategy, Factory, Command ハンドラーが登録されていることを検証
        Assert.Contains(services, d => d.ServiceType == typeof(IContractExecutorFactory) && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, d => d.ServiceType == typeof(TaggingContractService) && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Equal(4, services.Count(d => d.ServiceType == typeof(IContractExecutor)));
    }

    [Fact]
    public void AddTaggingAndDomainServices_RegistersExpectedServicesWithScopedLifetime()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddTaggingAndDomainServices();

        // Assert: 各サービスが Scoped で統一されて登録されていることを検証
        Assert.Contains(services, d => d.ServiceType == typeof(IDialogLauncher) && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, d => d.ServiceType == typeof(IItemTagService) && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, d => d.ServiceType == typeof(ITagEdgeService) && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, d => d.ServiceType == typeof(ITaggingService) && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, d => d.ServiceType == typeof(INotificationService) && d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void ExtensionMethods_ThrowArgumentNullException_WhenServicesIsNull()
    {
        IServiceCollection nullServices = null!;

        Assert.Throws<ArgumentNullException>(() => nullServices.AddDataProviders());
        Assert.Throws<ArgumentNullException>(() => nullServices.AddContractAndCommandServices());
        Assert.Throws<ArgumentNullException>(() => nullServices.AddTaggingAndDomainServices());
    }

    [Fact]
    public void ItemTagService_ThrowsArgumentNullException_WhenDbFactoryIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new ItemTagService(null!));
    }

    [Fact]
    public void NotificationService_ThrowsArgumentNullException_WhenDbFactoryIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new NotificationService(null!));
    }
}