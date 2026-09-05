using Microsoft.Extensions.DependencyInjection;

using SRNSMudApp.Data;
using SRNSMudApp.Data.Interceptors;
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
        Assert.Contains(services, d => d.ServiceType == typeof(ITaggingContractService) && d.Lifetime == ServiceLifetime.Scoped);
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
        Assert.Contains(services, d => d.ServiceType == typeof(ITimelineRecorder) && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, d => d.ServiceType == typeof(ITagWeightLedgerService) && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, d => d.ServiceType == typeof(ApplicationDbSaveChangesInterceptor) && d.Lifetime == ServiceLifetime.Singleton);
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
    public void NotificationService_ThrowsArgumentNullException_WhenDataProviderIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new NotificationService(null!));
    }

    [Fact]
    public void TagEdgeService_ThrowsArgumentNullException_WhenDbFactoryIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new TagEdgeService(null!));
    }

    [Fact]
    public void DataProviders_ThrowArgumentNullException_WhenDbFactoryIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new TagTableDataProvider(null!));
        Assert.Throws<ArgumentNullException>(() => new ItemCardDataProvider(null!));
        Assert.Throws<ArgumentNullException>(() => new AdminDataProvider(null!));
        Assert.Throws<ArgumentNullException>(() => new ContractDataProvider(null!));
        Assert.Throws<ArgumentNullException>(() => new ItemDetailDataProvider(null!));
        Assert.Throws<ArgumentNullException>(() => new UserDataProvider(null!));
        Assert.Throws<ArgumentNullException>(() => new HomeDataProvider(null!));
        Assert.Throws<ArgumentNullException>(() => new NotificationsDataProvider(null!));
        Assert.Throws<ArgumentNullException>(() => new TagCardDataProvider(null!));
        Assert.Throws<ArgumentNullException>(() => new TagTreeDataProvider(null!));
        Assert.Throws<ArgumentNullException>(() => new TagDetailDataProvider(null!));
        Assert.Throws<ArgumentNullException>(() => new TaggingService(null!));
        Assert.Throws<ArgumentNullException>(() => new ImportTagDataProvider(null!, new Moq.Mock<ITagEmbeddingService>().Object));
        Assert.Throws<ArgumentNullException>(() => new ImportTagDataProvider(new Moq.Mock<Microsoft.EntityFrameworkCore.IDbContextFactory<ApplicationDbContext>>().Object, null!));
        Assert.Throws<ArgumentNullException>(() => new ItemListDataProvider(null!, new Moq.Mock<ITagEmbeddingService>().Object));
        Assert.Throws<ArgumentNullException>(() => new ItemListDataProvider(new Moq.Mock<Microsoft.EntityFrameworkCore.IDbContextFactory<ApplicationDbContext>>().Object, null!));
        Assert.Throws<ArgumentNullException>(() => new TagDialogDataProvider(null!, new Moq.Mock<ITagEmbeddingService>().Object));
        Assert.Throws<ArgumentNullException>(() => new TagDialogDataProvider(new Moq.Mock<Microsoft.EntityFrameworkCore.IDbContextFactory<ApplicationDbContext>>().Object, null!));
        Assert.Throws<ArgumentNullException>(() => new ItemListExportService(null!));
        Assert.Throws<ArgumentNullException>(() => new SystemTagEnsurer(null!));
        Assert.Throws<ArgumentNullException>(() => new LinkPreviewService(null!));
    }

    [Fact]
    public void RequestInfo_InitializesCorrectlyAsRecord()
    {
        var info = new global::SRNSMudApp.Components.UI.RequestInfo
        {
            IsTaggingRequest = true,
            ProposedWeight = 5,
            TargetItemId = 10,
            TargetItemContent = "Item Content",
            TargetTagId = 20,
            TargetTagName = "TagName",
            Status = TradeStatus.Proposed,
            RequestType = TaggingRequestType.Add
        };

        Assert.True(info.IsTaggingRequest);
        Assert.Equal(5, info.ProposedWeight);
        Assert.Equal(10, info.TargetItemId);
        Assert.Equal("Item Content", info.TargetItemContent);
        Assert.Equal(20, info.TargetTagId);
        Assert.Equal("TagName", info.TargetTagName);
        Assert.Equal(TradeStatus.Proposed, info.Status);
        Assert.Equal(TaggingRequestType.Add, info.RequestType);
    }
}