using Microsoft.Extensions.DependencyInjection;

using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Commands;
using SRNSMudApp.Services.Contracts;
using SRNSMudApp.Services.Dialogs;

namespace SRNSMudApp.Extensions;

/// <summary>
///     DI コンテナへのサービス登録をモジュール化・整理するための拡張メソッド群。
///     Program.cs の肥大化を抑え、関心事ごとに登録を分離することで保守性を高める。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Blazor コンポーネント用の DataProvider 群を登録する。
    ///     各 UI コンポーネントと DbContext の直接依存を切り離すための Provider パターン。
    /// </summary>
    /// <param name="services">サービスコレクション。</param>
    /// <returns>チェーン呼び出し用のサービスコレクション。</returns>
    public static IServiceCollection AddDataProviders(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ITagCardDataProvider, TagCardDataProvider>();
        services.AddScoped<IItemCardDataProvider, ItemCardDataProvider>();
        services.AddScoped<IItemListDataProvider, ItemListDataProvider>();
        services.AddScoped<ITagTreeDataProvider, TagTreeDataProvider>();
        services.AddScoped<ITagTableDataProvider, TagTableDataProvider>();
        services.AddScoped<IHomeDataProvider, HomeDataProvider>();
        services.AddScoped<INotificationsDataProvider, NotificationsDataProvider>();
        services.AddScoped<IImportTagDataProvider, ImportTagDataProvider>();
        services.AddScoped<IItemDetailDataProvider, ItemDetailDataProvider>();
        services.AddScoped<ITagDialogDataProvider, TagDialogDataProvider>();
        services.AddScoped<ITagDetailDataProvider, TagDetailDataProvider>();
        services.AddScoped<IContractDataProvider, ContractDataProvider>();
        services.AddScoped<IUserDataProvider, UserDataProvider>();
        services.AddScoped<IAdminDataProvider, AdminDataProvider>();
        services.AddScoped<ITagDiagramDataProvider, TagDiagramDataProvider>();

        return services;
    }

    /// <summary>
    ///     タグ付けコントラクト (Strategy / Factory) およびコマンドハンドラー (Command Pattern) 関連サービスを登録する。
    /// </summary>
    /// <param name="services">サービスコレクション。</param>
    /// <returns>チェーン呼び出し用のサービスコレクション。</returns>
    public static IServiceCollection AddContractAndCommandServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 契約実行 Strategy (IContractExecutor) の登録
        services.AddScoped<IContractExecutor, GratisContractExecutor>();
        services.AddScoped<IContractExecutor, MutualContractExecutor>();
        services.AddScoped<IContractExecutor, TriggerContractExecutor>();
        services.AddScoped<IContractExecutor, BountyContractExecutor>();
        services.AddScoped<IContractExecutorFactory, ContractExecutorFactory>();
        services.AddScoped<TaggingContractService>();

        // コマンドハンドラー (Command Pattern) の登録
        services.AddScoped<ICommandHandler<ApproveTaggingRequestCommand, Result<string>>, ApproveTaggingRequestHandler>();
        services.AddScoped<ICommandHandler<RejectTaggingRequestCommand, Result<bool>>, RejectTaggingRequestHandler>();

        return services;
    }

    /// <summary>
    ///     タグ付けおよびコアのドメインサービス群を登録する。
    /// </summary>
    /// <param name="services">サービスコレクション。</param>
    /// <returns>チェーン呼び出し用のサービスコレクション。</returns>
    public static IServiceCollection AddTaggingAndDomainServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Dialog 起動の抽象化 (単体テスト用モック差し替えポイント)
        services.AddScoped<IDialogLauncher, DialogLauncher>();

        // エクスポート・通知・タグ関連ドメインサービス
        services.AddScoped<IItemListExportService, ItemListExportService>();
        services.AddScoped<IItemTagService, ItemTagService>();
        services.AddScoped<ITagEdgeService, TagEdgeService>();

        // 他のサービスに合わせて Scoped ライフタイムに統一 (IDbContextFactory からコンテキストを生成するため安全)
        services.AddScoped<ITaggingService, TaggingService>();
        services.AddScoped<ITaggingRequestActions, TaggingRequestActions>();
        services.AddScoped<ISystemTagEnsurer, SystemTagEnsurer>();
        services.AddScoped<INotificationService, NotificationService>();

        return services;
    }
}