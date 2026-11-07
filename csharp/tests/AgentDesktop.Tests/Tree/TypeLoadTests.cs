using System.Reflection;
using Xunit;

namespace AgentDesktop.Tests.Tree;

/// <summary>A [ThreadStatic] field whose type is the containing record struct fails to
/// load on the CoreCLR of .NET 8 and 9, in every JIT-hosted process that builds a
/// deadline. This is the gate: it forces every exported type's metadata to load, so a
/// struct that regains a self-referential thread field fails here. The scan reflects over
/// the whole assembly, so the trimming diagnostics it raises are expected and suppressed;
/// the gate runs under the JIT, where nothing is trimmed.</summary>
public class TypeLoadTests
{
    [Fact]
    public void EveryExportedTypeLoadsUnderTheJit()
    {
#pragma warning disable IL2026, IL2070, IL2072, IL2075
        var assembly = typeof(AgentDesktop.Core.Response.Envelope).Assembly;
        var failures = new List<string>();
        var types = new List<Type>();

        try
        {
            types.AddRange(assembly.GetExportedTypes());
        }
        catch (TypeLoadException ex)
        {
            failures.Add($"the exported type list did not build: {ex.Message}");
        }

        foreach (var type in types)
        {
            try
            {
                foreach (var method in type.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    _ = method.GetParameters();
                }

                if (!type.IsAbstract && !type.IsInterface && !type.ContainsGenericParameters)
                {
                    _ = Activator.CreateInstance(type);
                }
            }
            catch (Exception ex)
            {
                var root = ex;
                while (root.InnerException is not null) root = root.InnerException;
                if (root is TypeLoadException load)
                {
                    failures.Add($"{type.FullName}: {load.Message}");
                }
            }
        }

        Assert.NotEmpty(types);
        Assert.Empty(failures);
#pragma warning restore IL2026, IL2070, IL2072, IL2075
    }
}
