namespace SRNSMudApp.Components.Tag;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using SRNSMudApp.Services;

using Tag = SRNSMudApp.Data.Tag;

/// <summary>
///     TagAutocomplete コンポーネントのタグ検索ロジックを仲介する ViewModel。
///     プライマリコンストラクタにより依存性注入の定型句を簡素化。
/// </summary>
public sealed class TagAutocompleteViewModel(ITagSearchQueryService tagSearchQueryService)
{
    /// <summary>
    ///     カスタム検索関数が指定されていればそれを呼び出し、無ければフォールバック付きタグ検索を実行する。
    /// </summary>
    public async Task<IEnumerable<Tag>> SearchTagsAsync(
        string? value,
        Func<string?, CancellationToken, Task<IEnumerable<Tag>>>? customSearchFunc,
        CancellationToken token)
    {
        return customSearchFunc != null
            ? await customSearchFunc(value, token)
            : await tagSearchQueryService.SearchTagsWithFallbackAsync(value, token);
    }
}