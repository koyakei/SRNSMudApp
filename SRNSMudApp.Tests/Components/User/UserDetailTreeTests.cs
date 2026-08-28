using System.Text.Json;

using AngleSharp.Dom;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Components.User;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Components.User;

public sealed class UserDetailTreeTests : IAsyncLifetime
{
    private const string TreeTabText = "作成したタグツリー";
    private const string TestTagName = "MyUniqueTestTag_12345";
    private const string TestUserId = "treetest-user-id";

    private readonly BunitContext _ctx = new();
    private readonly Mock<IUserDataProvider> _userDataMock = new();

    public UserDetailTreeTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _userDataMock.Object);

        Bunit.TestDoubles.BunitAuthorizationContext authorization = _ctx.AddAuthorization();
        authorization.SetAuthorized("treetestuser");
        authorization.SetClaims(new System.Security.Claims.Claim(
            System.Security.Claims.ClaimTypes.NameIdentifier, TestUserId));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public async Task TreeTab_ShowsUserTag_InJqTreeJson()
    {
        var user = new ApplicationUser { Id = TestUserId, UserName = "treetestuser", Email = "treetest@example.com" };
        var tag = new SRNSMudApp.Data.Tag
        {
            Id = 100,
            Name = TestTagName,
            Content = "This is a test tag for tree visualization.",
            OwnerId = TestUserId
        };

        _ = _userDataMock.Setup(d => d.GetUserDetailAsync(TestUserId))
            .ReturnsAsync(new UserDetailPageData(user, [tag], []));

        var json = await ActivateTreeTabAndGetJson(TestUserId);

        Assert.NotNull(json);
        Assert.Contains($@"""id"":{tag.Id}", json);
        Assert.Contains($@"""name"":""{TestTagName}""", json);
    }

    [Fact]
    public async Task TreeTab_ShowsTagAtRootLevel_WhenParentIsOwnedByAnotherUser()
    {
        const string userAId = "user-a-id";
        const string userBTagName = "UserBForeignParentTag";
        const string userATagName = "UserATagWithForeignParent";
        const int tagAId = 200;

        var userA = new ApplicationUser { Id = userAId, UserName = "user_a", Email = "usera@example.com" };
        var tagA = new SRNSMudApp.Data.Tag { Id = tagAId, Name = userATagName, OwnerId = userAId, ParentTagId = 999 };

        _ = _userDataMock.Setup(d => d.GetUserDetailAsync(userAId))
            .ReturnsAsync(new UserDetailPageData(userA, [tagA], []));

        var json = await ActivateTreeTabAndGetJson(userAId);

        Assert.NotNull(json);
        using var document = JsonDocument.Parse(json!);
        var isRootLevel = document.RootElement.ValueKind == JsonValueKind.Array
                           && document.RootElement.EnumerateArray().Any(node =>
                               node.TryGetProperty("id", out JsonElement id) && id.GetInt32() == tagAId);
        Assert.True(isRootLevel, $"タグ {userATagName}(id={tagAId}) がルートレベルに存在しません。JSON: {json}");

        Assert.DoesNotContain(userBTagName, json);
    }

    private async Task<string?> ActivateTreeTabAndGetJson(string userId)
    {
        List<Bunit.JSRuntimeInvocation> jsInteropInvocations = [];
        _ = _ctx.JSInterop.SetupVoid("jqTreeInterop.init", invocation =>
        {
            jsInteropInvocations.Add(invocation);
            return true;
        });

        IRenderedComponent<UserDetail> component =
            _ctx.Render<UserDetail>(parameters => parameters.Add(p => p.UserId, userId));

        component.WaitForState(() => !component.Markup.Contains("mud-progress-circular"));

        await component.InvokeAsync(() =>
        {
            IEnumerable<IElement> candidates = component.FindAll("*")
                .Where(e => e.TextContent.Trim() == TreeTabText);
            IElement? tabLabel = candidates.LastOrDefault();
            if (tabLabel == null)
            {
                Assert.Fail($"タブ「{TreeTabText}」が見つかりません。");
            }
            tabLabel!.Click();
        });

        component.WaitForAssertion(() => Assert.NotEmpty(jsInteropInvocations));

        Bunit.JSRuntimeInvocation invocation =
            jsInteropInvocations.First(i => i.Identifier == "jqTreeInterop.init");
        return invocation.Arguments[1] as string;
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}