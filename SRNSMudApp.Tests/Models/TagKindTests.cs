using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using Xunit;

namespace SRNSMudApp.Tests.Models;

public class TagKindTests
{
    [Fact]
    public void GetKind_WhenGoodTag_ReturnsVotingReactionTag()
    {
        var tag = new Tag { Name = "good", IsSystem = true, OwnerId = "user-1" };
        var kind = tag.GetKind();

        Assert.IsType<VotingReactionTag>(kind.Value);
    }

    [Fact]
    public void GetKind_WhenBadTag_ReturnsVotingReactionTag()
    {
        var tag = new Tag { Name = "bad", IsSystem = true, OwnerId = "user-1" };
        var kind = tag.GetKind();

        Assert.IsType<VotingReactionTag>(kind.Value);
    }

    [Fact]
    public void GetKind_WhenSystemCategoryTag_ReturnsSystemClassificationTag()
    {
        var tag = new Tag { Name = "Programming", IsSystem = true, OwnerId = "system" };
        var kind = tag.GetKind();

        Assert.IsType<SystemClassificationTag>(kind.Value);
        Assert.Equal("Programming", ((SystemClassificationTag)kind.Value).Name);
    }

    [Fact]
    public void GetKind_WhenSystemTagWithoutSystemOwner_ReturnsSystemClassificationTag()
    {
        var tag = new Tag { Name = "Science", IsSystem = true, OwnerId = "admin" };
        var kind = tag.GetKind();

        Assert.IsType<SystemClassificationTag>(kind.Value);
    }

    [Fact]
    public void GetKind_WhenUserCustomTag_ReturnsUserCustomTag()
    {
        var tag = new Tag { Name = "MyTag", IsSystem = false, OwnerId = "user-1" };
        var kind = tag.GetKind();

        Assert.IsType<UserCustomTag>(kind.Value);
        Assert.Equal("user-1", ((UserCustomTag)kind.Value).OwnerId);
    }
}
