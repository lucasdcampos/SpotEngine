using System.Reflection;
using System.Linq;
using Xunit;
using System.Collections.Generic;
using System;

namespace Spot.Engine.Tests;

public class ArchitectureTests
{
    private static Assembly EngineAssembly => typeof(Spot.Engine.Application).Assembly;

    [Fact]
    public void LowLevelModules_DoNotReferenceHighLevelModules()
    {
        // Low-level modules (the old Framework) should not know about Scenes, ECS, or Assets.
        string[] lowLevelPrefixes = {
            "Spot.Engine.Input",
            "Spot.Engine.Audio",
            "Spot.Engine.Mathematics",
            "Spot.Engine.Platform",
            "Spot.Engine.Events",
            "Spot.Engine.IO"
        };

        string[] highLevelPrefixes = {
            "Spot.Engine.Scenes",
            "Spot.Engine.Assets",
            "Spot.Engine.Physics",
            "Spot.Engine.Animation" // If Animation is high level
        };

        var types = EngineAssembly.GetTypes();
        var failures = new List<string>();

        foreach (var type in types)
        {
            if (type.Namespace == null) continue;
            
            bool isLowLevel = false;
            foreach (var prefix in lowLevelPrefixes)
            {
                if (type.Namespace == prefix || type.Namespace.StartsWith(prefix + "."))
                {
                    isLowLevel = true;
                    break;
                }
            }

            if (!isLowLevel) continue;

            // Check fields, properties, methods for high-level references
            var references = new HashSet<Type>();
            
            // Just a basic check of public API to simulate architecture rules
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                references.Add(method.ReturnType);
                foreach (var param in method.GetParameters()) references.Add(param.ParameterType);
            }
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                references.Add(field.FieldType);
            }

            foreach (var refType in references)
            {
                if (refType.Namespace == null) continue;
                foreach (var highPrefix in highLevelPrefixes)
                {
                    if (refType.Namespace == highPrefix || refType.Namespace.StartsWith(highPrefix + "."))
                    {
                        failures.Add($"{type.FullName} references {refType.FullName}");
                    }
                }
            }
        }

        Assert.Empty(failures);
    }

    // Projects (.sptproj) are an authoring concept owned by Spot.Build: the runtime boots from game.manifest and only
    // knows scenes and assets, and DebugUI also runs as the runtime overlay.
    [Fact]
    public void RuntimeAssemblies_DoNotKnowAboutProjects()
    {
        Assembly[] runtime = { EngineAssembly, typeof(Spot.DebugUI.UI.ComponentScripts).Assembly };
        string[] projectTypes = { "Project", "ProjectConfig", "ProjectStructure" };

        var failures = new List<string>();
        foreach (Assembly assembly in runtime)
        {
            string name = assembly.GetName().Name!;
            if (assembly.GetReferencedAssemblies().Any(r => r.Name == "Spot.Build"))
                failures.Add($"{name} references Spot.Build");

            failures.AddRange(assembly.GetTypes()
                .Where(t => projectTypes.Contains(t.Name))
                .Select(t => $"{name} defines {t.FullName}"));
        }

        Assert.Empty(failures);
    }
}


