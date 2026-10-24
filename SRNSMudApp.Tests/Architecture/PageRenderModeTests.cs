using System.Reflection;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

using SRNSMudApp.Components;
using SRNSMudApp.Components.Pages;
using SRNSMudApp.Components.UserGroup;

namespace SRNSMudApp.Tests.Architecture;

/// <summary>
///     Blazor ページの @rendermode 設定を検証するアーキテクチャテスト。
///     Blazor Web App において、ページに @rendermode が指定されていない場合、
///     Static SSR (静的サーバーレンダリング) となりブラウザ上でボタンクリックやイベントハンドラが動作しなくなる。
///     Account 関連ページおよび静的エラーページを除き、全ての機能ページが InteractiveServer レンダリングモードを
///     宣言していることを保証する。
/// </summary>
public class PageRenderModeTests
{
    private static readonly HashSet<Type> StaticPages =
    [
        typeof(Error),
        typeof(NotFound)
    ];

    [Fact]
    public void UserGroupListPage_HasInteractiveServerRenderMode()
    {
        // Arrange & Act
        var renderModeAttr = typeof(UserGroupList)
            .GetCustomAttributes(typeof(RenderModeAttribute), inherit: true)
            .Cast<RenderModeAttribute>()
            .FirstOrDefault();

        // Assert: UserGroupList が InteractiveServer レンダリングモードを持つことを検証
        Assert.NotNull(renderModeAttr);
        Assert.IsType<InteractiveServerRenderMode>(renderModeAttr.Mode);
    }

    [Fact]
    public void EveryFeaturePage_MustDeclareInteractiveServerRenderMode()
    {
        // Arrange: App アセンブリ内の全ルーティング可能コンポーネントを取得
        var appAssembly = typeof(App).Assembly;
        var pageTypes = appAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IComponent).IsAssignableFrom(t))
            .Where(t => t.GetCustomAttributes(typeof(RouteAttribute), inherit: false).Any())
            .ToList();

        var missingRenderModes = new List<string>();

        // Act
        foreach (var pageType in pageTypes)
        {
            // Account ページ (ASP.NET Core Identity フォーム用 Static SSR) と静的エラーページは除外
            if (pageType.Namespace != null && pageType.Namespace.StartsWith("SRNSMudApp.Components.Account", StringComparison.Ordinal))
            {
                continue;
            }

            if (StaticPages.Contains(pageType))
            {
                continue;
            }

            var renderModeAttr = pageType
                .GetCustomAttributes(typeof(RenderModeAttribute), inherit: true)
                .Cast<RenderModeAttribute>()
                .FirstOrDefault();

            if (renderModeAttr == null)
            {
                missingRenderModes.Add($"{pageType.FullName} (No RenderMode)");
            }
            else if (renderModeAttr.Mode is not InteractiveServerRenderMode)
            {
                missingRenderModes.Add($"{pageType.FullName} (RenderMode is {renderModeAttr.Mode.GetType().Name}, expected InteractiveServerRenderMode)");
            }
        }

        // Assert: 漏れがあるページが存在しないことを検証
        Assert.True(
            missingRenderModes.Count == 0,
            $"Found {missingRenderModes.Count} feature page(s) missing '@rendermode InteractiveServer':{Environment.NewLine}" +
            string.Join(Environment.NewLine, missingRenderModes));
    }

    [Fact]
    public void EveryClientFeaturePage_MustDeclareInteractiveWebAssemblyRenderMode()
    {
        // Arrange: SRNSMudApp.Client アセンブリ内の全ルーティング可能コンポーネントを取得
        var clientAssembly = typeof(SRNSMudApp.Client.Pages.ItemDetailPage).Assembly;
        var pageTypes = clientAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IComponent).IsAssignableFrom(t))
            .Where(t => t.GetCustomAttributes(typeof(RouteAttribute), inherit: false).Any())
            .ToList();

        var missingRenderModes = new List<string>();

        // Act
        foreach (var pageType in pageTypes)
        {
            var renderModeAttr = pageType
                .GetCustomAttributes(typeof(RenderModeAttribute), inherit: true)
                .Cast<RenderModeAttribute>()
                .FirstOrDefault();

            if (renderModeAttr == null)
            {
                missingRenderModes.Add($"{pageType.FullName} (No RenderMode)");
            }
            else if (renderModeAttr.Mode is not InteractiveWebAssemblyRenderMode wasmMode)
            {
                missingRenderModes.Add($"{pageType.FullName} (RenderMode is {renderModeAttr.Mode.GetType().Name}, expected InteractiveWebAssemblyRenderMode)");
            }
            else if (wasmMode.Prerender)
            {
                missingRenderModes.Add($"{pageType.FullName} (InteractiveWebAssemblyRenderMode has prerender: true, expected prerender: false)");
            }
        }

        // Assert: 漏れがあるページが存在しないことを検証
        Assert.True(
            missingRenderModes.Count == 0,
            $"Found {missingRenderModes.Count} client page(s) missing '@rendermode @(new InteractiveWebAssemblyRenderMode(prerender: false))':{Environment.NewLine}" +
            string.Join(Environment.NewLine, missingRenderModes));
    }
}