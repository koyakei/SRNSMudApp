using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using Xunit;

namespace SRNSMudApp.Tests.Architecture;

/// <summary>
///     アーキテクチャ規約テスト: コンポーネントが ApplicationDbContext を直接注入していないことを検証する。
///     bUnit に依存しない純粋なリフレクションテストとして分離。
/// </summary>
public class DbContextInjectionRuleTests
{
    [Theory]
    [InlineData(typeof(TagList))]
    [InlineData(typeof(TagTable))]
    public void Component_ShouldNotInjectDbContextDirectly_ToPreventConcurrencyIssues(Type componentType)
    {
        // Arrange
        PropertyInfo[] properties =
            componentType.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        // Act
        var hasDirectDbContextInjection = properties.Any(p =>
            p.PropertyType == typeof(ApplicationDbContext) &&
            p.GetCustomAttributes(typeof(InjectAttribute), true).Length != 0);

        // Assert
        Assert.False(hasDirectDbContextInjection,
            $"{componentType.Name} should inject IDbContextFactory<ApplicationDbContext> instead of ApplicationDbContext directly to prevent InvalidOperationException during concurrent rendering.");
    }
}
