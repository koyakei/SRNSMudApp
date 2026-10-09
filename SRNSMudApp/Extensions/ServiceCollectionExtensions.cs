using Microsoft.Extensions.DependencyInjection;

using SRNSMudApp.Components.Account.Pages.Debug;
using SRNSMudApp.Components.Admin;
using SRNSMudApp.Components.Bounty;
using SRNSMudApp.Components.Contract;
using SRNSMudApp.Components.Diagram;
using SRNSMudApp.Components.Item;
using SRNSMudApp.Components.Layout;
using SRNSMudApp.Components.Pages;
using SRNSMudApp.Components.PublicOffer;
using SRNSMudApp.Components.Tag;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Components.User;
using SRNSMudApp.Components.UserGroup;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Auth;
using SRNSMudApp.Services.Commands;
using SRNSMudApp.Services.Contracts;
using SRNSMudApp.Services.Dialogs;
using SRNSMudApp.Services.Reports;
using SRNSMudApp.Services.Resolvers;

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

        services.AddMemoryCache();

        services.AddScoped<ITagCardDataProvider, TagCardDataProvider>();
        services.AddScoped<IItemCardDataProvider, ItemCardDataProvider>();
        services.AddScoped<IItemListDataProvider, ItemListDataProvider>();
        services.AddScoped<ITagTreeDataProvider, TagTreeDataProvider>();
        services.AddScoped<ITagTableDataProvider, TagTableDataProvider>();
        services.AddScoped<IHomeDataProvider, HomeDataProvider>();
        services.AddScoped<INotificationsDataProvider, NotificationsDataProvider>();
        services.AddScoped<IImportTagDataProvider, ImportTagDataProvider>();
        services.AddScoped<IItemDetailDataProvider, ItemDetailDataProvider>();
        services.AddScoped<ITagSearchQueryService, TagSearchQueryService>();
        services.AddScoped<ITagCommandService, TagCommandService>();
        services.AddScoped<ITagDetailDataProvider, TagDetailDataProvider>();
        services.AddScoped<ContractDataProvider>();
        services.AddScoped<IContractManagementDataProvider>(sp => sp.GetRequiredService<ContractDataProvider>());
        services.AddScoped<IBountyDataProvider>(sp => sp.GetRequiredService<ContractDataProvider>());
        services.AddScoped<IPublicOfferDataProvider>(sp => sp.GetRequiredService<ContractDataProvider>());
        services.AddScoped<IContractLookupDataProvider>(sp => sp.GetRequiredService<ContractDataProvider>());
        services.AddScoped<IUserDataProvider, UserDataProvider>();
        services.AddScoped<IAdminDataProvider, AdminDataProvider>();
        services.AddScoped<ITagDiagramDataProvider, TagDiagramDataProvider>();
        services.AddScoped<IUserGroupDataProvider, UserGroupDataProvider>();
        services.AddScoped<ITagHierarchyService, TagHierarchyService>();
        services.AddScoped<ITaggingImportDataProvider, TaggingImportDataProvider>();
        services.AddScoped<ITagLockService, TagLockService>();
        services.AddScoped<IRightAssetDataProvider, RightAssetDataProvider>();
        services.AddSingleton<IJpycTransactionVerifier, JpycTransactionVerifier>();
        services.AddScoped<IRightAssetPurchaseService, RightAssetPurchaseService>();

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
        services.AddScoped<IContractExecutor, MoveContractExecutor>();
        services.AddScoped<IContractExecutorFactory, ContractExecutorFactory>();
        services.AddScoped<ITaggingContractService, TaggingContractService>();
        services.AddScoped<TaggingContractService>(sp => (TaggingContractService)sp.GetRequiredService<ITaggingContractService>());

        // コマンドハンドラー (Command Pattern) の登録
        services.AddScoped<ICommandHandler<ApproveTaggingRequestCommand, Result<string>>, ApproveTaggingRequestHandler>();
        services.AddScoped<ICommandHandler<RejectTaggingRequestCommand, Result<bool>>, RejectTaggingRequestHandler>();
        services.AddScoped<ICommandHandler<ResolveContentReportCommand, Result<bool>>, ResolveContentReportHandler>();
        services.AddScoped<ICommandHandler<CreatePublicOfferCommand, Result<bool>>, CreatePublicOfferCommandHandler>();
        services.AddScoped<ICommandHandler<CreateBountyCommand, Result<bool>>, CreateBountyCommandHandler>();
        services.AddScoped<ICommandHandler<CreateTriggerContractCommand, Result<bool>>, CreateTriggerContractCommandHandler>();

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
        services.AddScoped<ITimelineRecorder, TimelineRecorder>();
        services.AddScoped<ITagWeightLedgerService, TagWeightLedgerService>();
        services.AddScoped<IItemListExportService, ItemListExportService>();
        services.AddScoped<ItemTagService>();
        services.AddScoped<IItemTagService>(sp => sp.GetRequiredService<ItemTagService>());
        services.AddScoped<ITagRelationToTagService>(sp => sp.GetRequiredService<ItemTagService>());
        services.AddScoped<IItemReplyService, ItemReplyService>();
        services.AddScoped<IItemReactionService, ItemReactionService>();
        // 登録順が元アイテム解決の優先順位になる。
        services.AddScoped<IItemSourceResolver, QuotedItemIdSourceResolver>();
        services.AddScoped<IItemSourceResolver, ItemSplitRequestSourceResolver>();
        services.AddScoped<IItemSourceResolver, ItemLinkSourceResolver>();
        services.AddScoped<IItemQuoteService, ItemQuoteService>();
        services.AddScoped<ITagEdgeService, TagEdgeService>();

        // 他のサービスに合わせて Scoped ライフタイムに統一 (IDbContextFactory からコンテキストを生成するため安全)
        services.AddScoped<TaggingService>();
        services.AddScoped<ITaggingService>(sp => sp.GetRequiredService<TaggingService>());
        services.AddScoped<IDirectTaggingService>(sp => sp.GetRequiredService<TaggingService>());
        services.AddScoped<ITagRequestRejectionService>(sp => sp.GetRequiredService<TaggingService>());
        services.AddScoped<ITaggingRequestActions, TaggingRequestActions>();
        services.AddScoped<ISystemTagEnsurer, SystemTagEnsurer>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IContentReportService, ContentReportService>();
        services.AddScoped<IItemSplitService, ItemSplitService>();
        services.AddScoped<ITagContentProposalService, TagContentProposalService>();
        services.AddScoped<ITagNameProposalService, TagNameProposalService>();
        services.AddScoped<IItemCardVoteCoordinator, ItemCardVoteCoordinator>();
        services.AddScoped<IItemCardSplitCoordinator, ItemCardSplitCoordinator>();
        services.AddScoped<IItemCardTagCoordinator, ItemCardTagCoordinator>();

        // タグ提案サービス
        services.AddScoped<ITagSuggestionService, TagSuggestionService>();
        services.AddScoped<ITagSimilarityService, TagSimilarityService>();

        // 内部リンク自動変換サービス
        services.AddScoped<IInternalLinkConversionService, InternalLinkConversionService>();

        // 通報対象 Strategy (IReportTargetHandler) および Factory の登録
        services.AddScoped<IReportTargetHandler, ItemReportTargetHandler>();
        services.AddScoped<IReportTargetHandler, TagReportTargetHandler>();
        services.AddScoped<IReportTargetHandlerFactory, ReportTargetHandlerFactory>();

        // テスト時に時刻固定を可能にする TimeProvider 抽象化
        services.AddSingleton(TimeProvider.System);

        // EF Core SaveChangesInterceptor
        services.AddSingleton<Data.Interceptors.ApplicationDbSaveChangesInterceptor>();

        // 初回デプロイ後の最初の登録ユーザーを Admin に自動昇格するサービス
        services.AddScoped<IFirstUserAdminService, FirstUserAdminService>();

        return services;
    }

    /// <summary>
    ///     Blazor コンポーネント用の ViewModel 群を登録する。
    ///     コンポーネントからビジネスロジックを分離し、単体テストを可能にする。
    /// </summary>
    public static IServiceCollection AddViewModels(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // ページ・Circuit 単位の ViewModel
        services.AddScoped<ItemDetailViewModel>();
        services.AddScoped<ItemImportViewModel>();
        services.AddScoped<ContractManagementViewModel>();
        services.AddScoped(sp => new UserDetailViewModel(
            sp.GetRequiredService<IUserDataProvider>(),
            sp.GetService<Microsoft.AspNetCore.Identity.UserManager<Data.ApplicationUser>>(),
            sp.GetService<IServiceScopeFactory>()));
        services.AddScoped(sp => new UserDetailActionViewModel(
            sp.GetRequiredService<IUserDataProvider>(),
            sp.GetService<Microsoft.AspNetCore.Identity.UserManager<Data.ApplicationUser>>(),
            sp.GetService<IServiceScopeFactory>()));
        services.AddScoped<TagDiagramCanvasViewModel>();
        services.AddScoped<UserManagementViewModel>();
        services.AddScoped<UserSearchViewModel>();
        services.AddScoped<TagDetailViewModel>();
        services.AddScoped<ReportManagerViewModel>();
        services.AddScoped<PublicOfferBoardViewModel>();
        services.AddScoped<QuotedItemListViewModel>();
        services.AddScoped<UserGroupMembersViewModel>();
        services.AddScoped<BountyBoardViewModel>();
        services.AddScoped<RightAssetOverviewViewModel>();
        services.AddScoped<NotificationBadgeViewModel>();
        services.AddScoped<PushNotificationPromptViewModel>();

        // STATE-01: ダイアログ用・短命アクション用 ViewModel を Transient に登録し、
        // Blazor Circuit 内での入力状態残留・ゴースト表示を防止する
        services.AddTransient<AddItemViewModel>();
        services.AddTransient<ItemEditViewModel>();
        services.AddTransient<ItemTagChipActionViewModel>();
        services.AddTransient<TaggingRequestActionViewModel>();
        services.AddTransient<ItemCardActionViewModel>();
        services.AddTransient<ProposeContractViewModel>();
        services.AddTransient<RequestTagPermissionViewModel>();
        services.AddTransient<TagAddViewModel>();
        services.AddTransient<ReportDetailActionViewModel>();
        services.AddTransient<TriggerPublicOfferViewModel>();
        services.AddTransient<CreatePublicOfferViewModel>();
        services.AddTransient<QuoteItemViewModel>();
        services.AddTransient<TagResolutionViewModel>();
        services.AddTransient<TagLinkReplaceViewModel>();
        services.AddTransient<TagEditViewModel>();
        services.AddTransient<PurchaseRightAssetViewModel>();
        services.AddTransient<TagContentProposalViewModel>();
        services.AddTransient<TagNameProposalViewModel>();
        services.AddTransient<ReportContentViewModel>();
        services.AddTransient<UserGroupCreateEditViewModel>();
        services.AddTransient<BountyCreateViewModel>();
        services.AddTransient<FulfillBountyViewModel>();
        services.AddTransient<AttachTagToEdgeViewModel>();
        services.AddTransient<TaggingRequestThreadViewModel>();
        services.AddTransient<ReactionCommentViewModel>();
        services.AddTransient<CreateEdgeViewModel>();
        services.AddTransient<TagEdgeInspectorViewModel>();
        services.AddTransient<MakeMeAdminViewModel>();
        services.AddTransient<AddTagPageViewModel>();
        services.AddTransient<ImportTagViewModel>();
        services.AddTransient<GenericTagEditorViewModel>();
        services.AddTransient<ImportTaggingViewModel>();
        services.AddTransient<TagListViewModel>();
        services.AddTransient<TagSearchPageViewModel>();
        services.AddTransient<TagManagementViewModel>();
        services.AddTransient<InvitationManagerViewModel>();
        services.AddTransient<RequireConfirmedAccountViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<QuotedItemPreviewViewModel>();
        services.AddTransient<ResourceListViewModel>();
        services.AddTransient<TagAutocompleteViewModel>();
        services.AddTransient<ItemTagTableViewModel>();
        services.AddTransient<TagCardViewModel>();
        services.AddTransient<ItemListViewModel>();
        services.AddTransient<TagTableViewModel>();
        services.AddTransient<TagTreeViewModel>();
        services.AddTransient<NotificationsViewModel>();
        services.AddTransient<TagDiagramPageViewModel>();
        services.AddTransient<NavMenuViewModel>();

        return services;
    }
}