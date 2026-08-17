#region

using Bunit;

using Microsoft.Extensions.DependencyInjection;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Tag;

#endregion

namespace SRNSMudApp.Tests.Components.Tag;

public class BaseTagChipTests : TestContext
{
    public BaseTagChipTests()
    {
        _ = Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void BaseTagChip_ShouldRenderTagNameAndWeight()
    {
        // Arrange
        var tagName = "TestTag";
        var weight = 5;
        
        // Act
        IRenderedComponent<BaseTagChip> component = RenderComponent<BaseTagChip>(parameters => parameters
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
          <span class=""d-flex align-center"">
            <a class=""chip-link mr-1"" >{tagName}</a>
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
        IRenderedComponent<BaseTagChip> component = RenderComponent<BaseTagChip>(parameters => parameters
            .Add(p => p.TagName, tagName)
            .Add(p => p.OwnerName, ownerName)
            .Add(p => p.ActionContent, builder => builder.AddMarkupContent(0, "<div class=\"action-mock\">Action</div>"))
        );

        // Assert
        Assert.Contains(ownerName, component.Markup);
        Assert.Contains("action-mock", component.Markup);
    }
}
