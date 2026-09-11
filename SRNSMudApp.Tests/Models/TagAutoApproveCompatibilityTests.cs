using System.Reflection;

using SRNSMudApp.Data;

namespace SRNSMudApp.Tests.Models;

public class TagAutoApproveCompatibilityTests
{
    [Fact]
    public void LegacyAutoApproveUserGroupId_IsMarkedObsolete()
    {
#pragma warning disable CS0618 // Obsolete 属性の付与対象を明示的に検証するため意図的に参照する
        PropertyInfo property = typeof(Tag).GetProperty(nameof(Tag.AutoApproveUserGroupId))!;
#pragma warning restore CS0618

        ObsoleteAttribute? obsolete = property.GetCustomAttribute<ObsoleteAttribute>();

        Assert.NotNull(obsolete);
        Assert.Contains("AutoApproveUserGroups", obsolete!.Message);
    }
}