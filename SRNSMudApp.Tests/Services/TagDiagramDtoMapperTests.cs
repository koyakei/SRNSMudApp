namespace SRNSMudApp.Tests.Services;

using System;
using System.Collections.Generic;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

using Xunit;

public class TagDiagramDtoMapperTests
{
    private readonly TagDiagramDtoMapper _mapper = new();

    [Fact]
    public void MapToTagDto_WhenValidTag_MapsCorrectly()
    {
        // Arrange
        var tag = new Tag
        {
            Id = 1,
            Name = "テストタグ",
            Content = "説明文",
            OwnerId = "u1",
            Owner = new ApplicationUser { Id = "u1", UserName = "Alice" },
            IsPrivate = false
        };

        // Act
        TagDto dto = _mapper.MapToTagDto(tag);

        // Assert
        Assert.Equal(1, dto.Id);
        Assert.Equal("テストタグ", dto.Name);
        Assert.Equal("説明文", dto.Content);
        Assert.Equal("Alice", dto.OwnerUserName);
        Assert.Equal("u1", dto.OwnerUserId);
    }

    [Fact]
    public void MapToTagDto_WhenNullTag_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _mapper.MapToTagDto(null!));
    }

    [Fact]
    public void MapToAttachmentDto_WhenValidAttachment_MapsCorrectly()
    {
        // Arrange
        var att = new TagEdgeTagAttachment
        {
            Id = 42,
            TagEdgeId = 10,
            TagId = 5,
            Tag = new Tag { Id = 5, Name = "付与タグ", OwnerId = "u2" },
            OwnerId = "u2",
            Owner = new ApplicationUser { Id = "u2", UserName = "Bob" },
            Weight = 3,
            CreatedDate = DateTime.UtcNow
        };

        // Act
        TagEdgeAttachmentDto dto = _mapper.MapToAttachmentDto(att);

        // Assert
        Assert.Equal(42, dto.Id);
        Assert.Equal(10, dto.EdgeId);
        Assert.Equal(5, dto.TagId);
        Assert.Equal("付与タグ", dto.TagName);
        Assert.Equal("Bob", dto.OwnerUserName);
        Assert.Equal(3, dto.Weight);
    }

    [Fact]
    public void MapToAttachmentDto_WhenNullAttachment_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _mapper.MapToAttachmentDto(null!));
    }
}