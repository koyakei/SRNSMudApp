namespace SRNSMudApp.Tests.Client;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Client.Models;
using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Client.Pages;
using SRNSMudApp.Client.Services;

using Xunit;

public class TagSearchPageTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemListApiClient> _apiClientMock = new();

    public TagSearchPageTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddScoped(_ => _apiClientMock.Object);
        _ctx.Render<MudPopoverProvider>();
    }

    [Fact]
    public void WhenInitialized_RendersSearchInputAndHeader()
    {
        // Act
        var cut = _ctx.Render<TagSearchPage>();

        // Assert
        Assert.Contains("タグ検索 (WASM)", cut.Markup);
        var autocomplete = cut.FindComponent<MudAutocomplete<TagSuggestion>>();
        Assert.NotNull(autocomplete);
    }

    [Fact]
    public async Task WhenTagSelected_LoadsAndDisplaysTagDetail()
    {
        // Arrange
        var sampleTag = new TagSuggestion(42, "AI", "testuser");
        var sampleDetail = new TagDetailDto(
            Id: 42,
            Name: "AI",
            Content: "人工知能のタグ説明",
            OwnerUserId: "u1",
            OwnerUserName: "testuser",
            Score: 100,
            IsPrivate: false,
            TargetUserGroupName: null,
            IsFollowing: false,
            AutoAcceptIncomingTaggingRequests: false,
            IsLocked: false,
            RelatedItems: [],
            RelatedTags: [new TagDto(1, "Technology", null, null, null, 50, false, null)],
            PendingRequests: []
        );

        _apiClientMock
            .Setup(c => c.GetTagDetailAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sampleDetail);

        var cut = _ctx.Render<TagSearchPage>();
        var autocomplete = cut.FindComponent<MudAutocomplete<TagSuggestion>>();

        // Act
        await cut.InvokeAsync(async () =>
        {
            await autocomplete.Instance.ValueChanged.InvokeAsync(sampleTag);
        });

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("AI", cut.Markup);
            Assert.Contains("人工知能のタグ説明", cut.Markup);
            Assert.Contains("Technology", cut.Markup);
            Assert.Contains("testuser", cut.Markup);
            Assert.Contains("Weight: 100", cut.Markup);
        });
    }

    [Fact]
    public async Task WhenNavigateDetailClicked_NavigatesToTagDetailPage()
    {
        // Arrange
        var navMan = _ctx.Services.GetRequiredService<NavigationManager>();
        var sampleTag = new TagSuggestion(42, "AI", "testuser");
        var sampleDetail = new TagDetailDto(
            Id: 42,
            Name: "AI",
            Content: "説明",
            OwnerUserId: "u1",
            OwnerUserName: "testuser",
            Score: 100,
            IsPrivate: false,
            TargetUserGroupName: null,
            IsFollowing: false,
            AutoAcceptIncomingTaggingRequests: false,
            IsLocked: false,
            RelatedItems: [],
            RelatedTags: [],
            PendingRequests: []
        );

        _apiClientMock
            .Setup(c => c.GetTagDetailAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sampleDetail);

        var cut = _ctx.Render<TagSearchPage>();
        var autocomplete = cut.FindComponent<MudAutocomplete<TagSuggestion>>();

        await cut.InvokeAsync(async () =>
        {
            await autocomplete.Instance.ValueChanged.InvokeAsync(sampleTag);
        });

        // Act
        var detailBtn = cut.Find("[data-testid='go-to-tag-detail-btn']");
        detailBtn.Click();

        // Assert
        Assert.EndsWith("/Tag/WasmDetail/42", navMan.Uri);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}