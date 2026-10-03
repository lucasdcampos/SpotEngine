using System.Linq;
using System.Reflection;
using Spot.Engine.Scenes;

namespace Spot.Engine.Tests;

public class ComponentMenuTests
{
    // The Add Component menu groups components by category; one without would land in a catch-all group.
    [Fact]
    public void EveryAddableEngineComponent_DeclaresACategory()
    {
        var uncategorized = typeof(Component).Assembly.GetTypes()
            .Select(t => (Type: t, Menu: t.GetCustomAttribute<ComponentMenuAttribute>()))
            .Where(x => x.Menu is { Addable: true } && string.IsNullOrEmpty(x.Menu.Category))
            .Select(x => x.Type.Name)
            .ToList();

        Assert.Empty(uncategorized);
    }
}
