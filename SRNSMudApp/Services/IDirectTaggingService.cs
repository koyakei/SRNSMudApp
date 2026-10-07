using SRNSMudApp.Data;

namespace SRNSMudApp.Services;

/// <summary>
///     IDirectTaggable なエンティティ（TaggingRequestEntityなど）に対する直接タグ追加・削除を担当するインターフェース（ISP 準拠）。
/// </summary>
public interface IDirectTaggingService
{
    /// <summary>
    ///     対象エンティティにタグを直接追加する。
    /// </summary>
    Task AddTagAsync<T>(int entityId, int tagId) where T : class, IDirectTaggable;

    /// <summary>
    ///     対象エンティティからタグを直接削除する。
    /// </summary>
    Task RemoveTagAsync<T>(int entityId, int tagId) where T : class, IDirectTaggable;
}