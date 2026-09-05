using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Services.Commands;

/// <summary>
///     タグ付けリクエストを承認するコマンド。
/// </summary>
/// <param name="RequestId">リクエスト（コントラクト）ID。</param>
/// <param name="CurrentUserId">承認操作を実行するユーザー ID。</param>
/// <param name="FulfillerAssetId">バウンティ契約等で提供するアセット ID（任意）。</param>
public record ApproveTaggingRequestCommand(int RequestId, string CurrentUserId, int? FulfillerAssetId = null);

/// <summary>
///     タグ付けリクエストを却下するコマンド。
/// </summary>
/// <param name="RequestId">リクエスト ID。</param>
/// <param name="CurrentUserId">却下操作を実行するユーザー ID。</param>
/// <param name="Reason">却下理由コメント（任意）。</param>
public record RejectTaggingRequestCommand(int RequestId, string CurrentUserId, string? Reason = null);

/// <summary>
///     タグ付けリクエスト承認コマンドのハンドラー。
/// </summary>
/// <param name="contractService">契約処理サービス。</param>
public class ApproveTaggingRequestHandler(ITaggingContractService contractService)
    : ICommandHandler<ApproveTaggingRequestCommand, Result<string>>
{
    private readonly ITaggingContractService _contractService =
        contractService ?? throw new ArgumentNullException(nameof(contractService));

    /// <inheritdoc />
    public async Task<Result<string>> HandleAsync(ApproveTaggingRequestCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await _contractService.AcceptContractAsync(command.RequestId, command.CurrentUserId, command.FulfillerAssetId);
    }
}

/// <summary>
///     タグ付けリクエスト却下コマンドのハンドラー。
/// </summary>
/// <param name="taggingService">タグサービス。</param>
public class RejectTaggingRequestHandler(ITaggingService taggingService)
    : ICommandHandler<RejectTaggingRequestCommand, Result<bool>>
{
    private readonly ITaggingService _taggingService =
        taggingService ?? throw new ArgumentNullException(nameof(taggingService));

    /// <inheritdoc />
    public async Task<Result<bool>> HandleAsync(RejectTaggingRequestCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await _taggingService.RejectRequestAsync(command.RequestId, command.CurrentUserId, command.Reason);
        return new Success<bool>(true);
    }
}