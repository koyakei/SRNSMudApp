namespace SRNSMudApp.Tests.Client;

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Client.Pages;
using SRNSMudApp.Client.Services;

using Xunit;

public class TagDetailPageTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemListApiClient> _apiClientMock = new();
    private readonly Mock<AuthenticationStateProvider> _authMock = new();

    public TagDetailPageTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddScoped(_ => _apiClientMock.Object);
        _ctx.Services.AddCascadingAuthenticationState();
        _ctx.Services.AddSingleton(_authMock.Object);

        // デフォルト: 匿名ユーザー
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        _authMock.Setup(p => p.GetAuthenticationStateAsync())
            .ReturnsAsync(new AuthenticationState(anonymous));
    }

    private void SetCurrentUser(string userId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        foreach (var r in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, r));
        }
        var identity = new ClaimsIdentity(claims, "test");
        var user = new ClaimsPrincipal(identity);
        _authMock.Setup(p => p.GetAuthenticationStateAsync())
            .ReturnsAsync(new AuthenticationState(user));
    }

    private static TagDetailDto CreateSampleDetail(
        int id = 1,
        string name = "テスト用タグ",
        string? ownerUserId = "alice-id",
        bool isLocked = false,
        bool isFollowing = false)
    {
        return new TagDetailDto(
            Id: id,
            Name: name,
            Content: "テストタグの詳細説明文です",
            OwnerUserId: ownerUserId,
            OwnerUserName: "alice",
            Score: 15,
            IsPrivate: false,
            TargetUserGroupName: null,
            IsFollowing: isFollowing,
            AutoAcceptIncomingTaggingRequests: true,
            IsLocked: isLocked,
            RelatedItems: [new ItemDto(10, "関連アイテム本文", "bob", DateTimeOffset.UtcNow, false, [], "{}")],
            RelatedTags: [new TagDto(2, "子タグ1", "説明", "alice", null, 3, false, null)],
            PendingRequests: []
        );
    }

    [Fact]
    public void WhenTagNotFound_RendersNotFoundMessage()
    {
        // Arrange
        _apiClientMock
            .Setup(c => c.GetTagDetailAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TagDetailDto?)null);

        // Act
        IRenderedComponent<TagDetailPage> cut = _ctx.Render<TagDetailPage>(p => p.Add(x => x.TagId, 999));

        // Assert
        Assert.Contains("タグが見つかりませんでした", cut.Markup);
    }

    [Fact]
    public void WhenTagExists_RendersTagDetailAndRelatedData()
    {
        // Arrange
        var detail = CreateSampleDetail(1, "テスト用タグ", "alice-id", false, true);

        _apiClientMock
            .Setup(c => c.GetTagDetailAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        // Act
        IRenderedComponent<TagDetailPage> cut = _ctx.Render<TagDetailPage>(p => p.Add(x => x.TagId, 1));

        // Assert
        Assert.Contains("テスト用タグ", cut.Markup);
        Assert.Contains("テストタグの詳細説明文です", cut.Markup);
        Assert.Contains("15", cut.Markup);
        Assert.Contains("関連アイテム (1)", cut.Markup);
        Assert.Contains("関連タグ (1)", cut.Markup);
        Assert.Contains("子タグ1", cut.Markup);
    }

    [Fact]
    public async Task WhenToggleFollowClicked_TogglesFollowStatus()
    {
        // Arrange
        SetCurrentUser("user-1");
        var detail = CreateSampleDetail(1, "テスト用タグ", "alice-id", false, false);

        _apiClientMock
            .Setup(c => c.GetTagDetailAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);
        _apiClientMock
            .Setup(c => c.ToggleTagFollowAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        IRenderedComponent<TagDetailPage> cut = _ctx.Render<TagDetailPage>(p => p.Add(x => x.TagId, 1));
        var followBtn = cut.Find("[data-testid='toggle-follow-btn']");
        Assert.Contains("フォロー", followBtn.TextContent);

        // Act
        await cut.InvokeAsync(() => followBtn.Click());

        // Assert
        _apiClientMock.Verify(c => c.ToggleTagFollowAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains("フォロー解除", cut.Markup);
    }

    [Fact]
    public async Task WhenOwner_CanEditTagAndSave()
    {
        // Arrange
        SetCurrentUser("alice-id");

        var detail = CreateSampleDetail(1, "テスト用タグ", "alice-id", false, false);
        var updatedDetail = CreateSampleDetail(1, "更新後タグ", "alice-id", false, false);

        _apiClientMock
            .SetupSequence(c => c.GetTagDetailAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail)
            .ReturnsAsync(updatedDetail);

        _apiClientMock
            .Setup(c => c.UpdateTagAsync(1, It.IsAny<UpdateTagRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        IRenderedComponent<TagDetailPage> cut = _ctx.Render<TagDetailPage>(p => p.Add(x => x.TagId, 1));

        // Act: 編集ボタンをクリック
        var editBtn = cut.Find("[data-testid='start-edit-btn']");
        await cut.InvokeAsync(() => editBtn.Click());

        // フォームが表示される
        Assert.NotNull(cut.Find("[data-testid='tag-edit-form']"));

        // 保存をクリック
        var saveBtn = cut.Find("[data-testid='save-edit-btn']");
        await cut.InvokeAsync(() => saveBtn.Click());

        // Assert
        _apiClientMock.Verify(c => c.UpdateTagAsync(1, It.Is<UpdateTagRequest>(r => r.Name == "テスト用タグ"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WhenOwner_CanDeleteTag()
    {
        // Arrange
        SetCurrentUser("alice-id");
        var navMan = _ctx.Services.GetRequiredService<NavigationManager>();

        var detail = CreateSampleDetail(1, "テスト用タグ", "alice-id", false, false);

        _apiClientMock
            .Setup(c => c.GetTagDetailAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        _apiClientMock
            .Setup(c => c.DeleteTagAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        IRenderedComponent<TagDetailPage> cut = _ctx.Render<TagDetailPage>(p => p.Add(x => x.TagId, 1));

        // Act: 削除ボタンをクリックして確認ボックスを表示
        var deleteBtn = cut.Find("[data-testid='delete-tag-btn']");
        await cut.InvokeAsync(() => deleteBtn.Click());

        Assert.NotNull(cut.Find("[data-testid='delete-confirm-box']"));

        // 削除実行ボタンをクリック
        var confirmDeleteBtn = cut.Find("[data-testid='confirm-delete-btn']");
        await cut.InvokeAsync(() => confirmDeleteBtn.Click());

        // Assert
        _apiClientMock.Verify(c => c.DeleteTagAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        Assert.EndsWith("/Tag/WasmList", navMan.Uri);
    }

    [Fact]
    public async Task WhenAdmin_CanToggleTagLock()
    {
        // Arrange
        SetCurrentUser("admin-1", "Admin");

        var detail = CreateSampleDetail(1, "テスト用タグ", "alice-id", isLocked: false);
        var lockedDetail = CreateSampleDetail(1, "テスト用タグ", "alice-id", isLocked: true);

        _apiClientMock
            .SetupSequence(c => c.GetTagDetailAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail)
            .ReturnsAsync(lockedDetail);

        _apiClientMock
            .Setup(c => c.ToggleTagLockAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        IRenderedComponent<TagDetailPage> cut = _ctx.Render<TagDetailPage>(p => p.Add(x => x.TagId, 1));

        // Act: ロックボタンをクリック
        var lockBtn = cut.Find("[data-testid='toggle-tag-lock-btn']");
        await cut.InvokeAsync(() => lockBtn.Click());

        // Assert
        _apiClientMock.Verify(c => c.ToggleTagLockAsync(1, It.IsAny<CancellationToken>()), Times.Once);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}