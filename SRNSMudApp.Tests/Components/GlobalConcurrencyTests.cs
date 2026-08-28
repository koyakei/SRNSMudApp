#region

using System.Reflection;

using Microsoft.AspNetCore.Components;

using SRNSMudApp.Components;
using SRNSMudApp.Data;

using Xunit.Abstractions;

#endregion

namespace SRNSMudApp.Tests.Components;

public class GlobalConcurrencyTests(ITestOutputHelper output)
{
    [Fact]
    public void NoComponentShouldInjectDbContextDirectly()
    {
        // Arrange
        // Get all types in the SRNSMudApp assembly that implement IComponent
        Assembly assembly = typeof(App).Assembly;
        var componentTypes = assembly.GetTypes()
            .Where(t => typeof(IComponent).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        List<string> failingComponents = [];

        // Act
        foreach (Type type in componentTypes.Where(type =>
                     type.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                         .Any(p => p.PropertyType == typeof(ApplicationDbContext) &&
                                   p.GetCustomAttributes(typeof(InjectAttribute), true).Length != 0)))
        {
            failingComponents.Add(type.Name);
            output.WriteLine(
                $"Component {type.Name} injects ApplicationDbContext directly. Use IDbContextFactory instead.");
        }

        // Assert
        Assert.Empty(failingComponents);
    }
}