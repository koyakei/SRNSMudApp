using System.Reflection;

using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

using MudBlazor;

using SRNSMudApp.Data;

namespace SRNSMudApp.Tests.Architecture;

/// <summary>
///     アーキテクチャ規約テスト: コンポーネントが ApplicationDbContext を直接注入していないことを検証する。
///     bUnit に依存しない純粋なリフレクションテストとして分離。
/// </summary>
public class DbContextInjectionRuleTests
{
    [Fact]
    public void Components_ShouldNotInjectDbContextDirectly_ToPreventConcurrencyIssues()
    {
        Type[] violatingComponents = GetServerComponents()
            .Where(HasForbiddenDbContextInjection)
            .ToArray();

        Assert.Empty(violatingComponents);
    }

    [Fact]
    public void Components_ShouldUseDialogLauncherInsteadOfMudDialogService()
    {
        Type[] violatingComponents = GetServerComponents()
            .Where(HasForbiddenDialogServiceInjection)
            .ToArray();

        Assert.Empty(violatingComponents);
    }

    private static Type[] GetServerComponents() =>
        typeof(ApplicationDbContext).Assembly
            .GetTypes()
            .Where(type =>
                type.IsClass &&
                !type.IsAbstract &&
                typeof(ComponentBase).IsAssignableFrom(type) &&
                type.Namespace?.StartsWith("SRNSMudApp.Components", StringComparison.Ordinal) is true)
            .ToArray();

    private static bool HasForbiddenDbContextInjection(Type componentType) =>
        GetInjectedProperties(componentType).Any(property =>
            property.PropertyType == typeof(ApplicationDbContext) ||
            property.PropertyType == typeof(IDbContextFactory<ApplicationDbContext>));

    private static bool HasForbiddenDialogServiceInjection(Type componentType) =>
        GetInjectedProperties(componentType).Any(property => property.PropertyType == typeof(IDialogService));

    private static PropertyInfo[] GetInjectedProperties(Type componentType) =>
        componentType.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(property => property.GetCustomAttributes(typeof(InjectAttribute), inherit: true).Length != 0)
            .ToArray();
}