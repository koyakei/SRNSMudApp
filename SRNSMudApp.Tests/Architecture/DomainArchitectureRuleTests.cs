#region

using System.Reflection;

using SRNSMudApp.Models;

#endregion

namespace SRNSMudApp.Tests.Architecture;

/// <summary>
///     アーキテクチャ規約テスト:
///     ドメインモデルおよび純粋ドメインポリシー（SRNSMudApp.Models 配下のクラス群）が
///     Entity Framework Core や DbContext への直接依存を持たないことを検証する。
/// </summary>
public sealed class DomainArchitectureRuleTests
{
    [Fact]
    public void DomainModelsAndPolicies_ShouldNotDependOnEntityFrameworkCore()
    {
        Assembly assembly = typeof(ItemSplitTextPolicy).Assembly;

        Type[] domainTypes = [.. assembly.GetTypes()
            .Where(t => t.Namespace == "SRNSMudApp.Models" && !t.IsNested)];

        List<string> violations = [];

        foreach (Type type in domainTypes)
        {
            // プロパティ、フィールド、メソッドの型が EntityFrameworkCore を参照していないか検査
            bool hasEfDependency = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Any(member => member switch
                {
                    PropertyInfo p => p.PropertyType.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) is true,
                    FieldInfo f => f.FieldType.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) is true,
                    MethodInfo m => m.ReturnType.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) is true
                                    || m.GetParameters().Any(param => param.ParameterType.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) is true),
                    _ => false
                });

            if (hasEfDependency)
            {
                violations.Add(type.FullName ?? type.Name);
            }
        }

        Assert.Empty(violations);
    }
}