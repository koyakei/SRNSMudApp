#region
using System;
using System.Threading.Tasks;
using Bunit;

using SRNSMudApp.Tests.TestSupport;
using MudBlazor.Services;
using SRNSMudApp.Components.Tag;
using Xunit;
#endregion

namespace SRNSMudApp.Tests.Components.Tag;

// BunitContextの継承をやめ、IAsyncDisposableを実装します
public class BaseTagChipTests : IAsyncDisposable
{
    // BunitContextのインスタンスをプライベートフィールドとして保持（コンポジション・パターン）
    private readonly BunitContext _ctx;

    public BaseTagChipTests()
    {
        _ctx = new BunitContext();
        _ctx.Services.AddMudServices().AddSrnsComponentServices();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // 非同期でBunitContextを破棄し、MudBlazorのKeyInterceptorServiceのエラーを防ぐ
    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public void BaseTagChip_ShouldRenderTagNameAndWeight()
    {
        // Arrange
        var tagName = "TestTag";
        var weight = 5;

        // Act
        // Render ではなく _ctx.RenderComponent を使用します
        IRenderedComponent<BaseTagChip> component = _ctx.Render<BaseTagChip>(parameters => parameters
            .Add(p => p.TagName, tagName)
            .Add(p => p.Weight, weight)
        );

        // Assert
        component.MarkupMatches($@"
<span id="""" class=""position-relative d-inline-flex align-center"">
  <div class=""mud-chip-container"" id:ignore>
    <div tabindex=""-1"" class=""mud-chip mud-chip-filled mud-chip-size-small mud-chip-color-primary custom-chip"">
      <span class=""mud-chip-content"">
        <div class=""d-flex align-center"">
          <span class=""d-flex align-center mr-1"">
            <a class=""chip-link mr-1"" >{tagName}</a>
          </span>
          <span class=""d-flex align-center"">
            <span class=""mx-1 font-weight-bold"">{weight}</span>
          </span>
        </div>
      </span>
    </div>
  </div>
</span>
");
    }

    [Fact]
    public void BaseTagChip_ShouldRenderOwnerNameAndActionContent_WhenProvided()
    {
        // Arrange
        var tagName = "TestTag";
        var ownerName = "user123";

        // Act
        // Render ではなく _ctx.RenderComponent を使用します
        IRenderedComponent<BaseTagChip> component = _ctx.Render<BaseTagChip>(parameters => parameters
            .Add(p => p.TagName, tagName)
            .Add(p => p.OwnerName, ownerName)
            .Add(p => p.ActionContent,
                builder => builder.AddMarkupContent(0, "<div class=\"action-mock\">Action</div>"))
        );

        // Assert
        Assert.Contains(ownerName, component.Markup);
        Assert.Contains("action-mock", component.Markup);
    }
}