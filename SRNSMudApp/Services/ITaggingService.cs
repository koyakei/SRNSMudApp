using SRNSMudApp.Data;

namespace SRNSMudApp.Services;

/// <summary>
///     タグ付け操作およびリクエスト却下の統合インターフェース。
///     ISP 準拠のため <see cref="IDirectTaggingService"/> および <see cref="ITagRequestRejectionService"/> に細分化されている。
/// </summary>
public interface ITaggingService : IDirectTaggingService, ITagRequestRejectionService
{
}