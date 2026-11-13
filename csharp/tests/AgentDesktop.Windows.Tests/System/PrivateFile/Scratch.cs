namespace AgentDesktop.Windows.Tests;

/// <summary>A scratch root under the system temporary directory, created with a unique name and removed
/// when the test releases it.</summary>
internal sealed class Scratch : IDisposable
{
    internal string Root { get; }

    private Scratch(string root)
    {
        Root = root;
        Directory.CreateDirectory(root);
    }

    internal static Scratch New(string name) =>
        new(Path.Combine(
            Path.GetTempPath(),
            $"agent-desktop-pf-{name}-{Environment.ProcessId}-{Guid.NewGuid():N}"));

    /// <summary>Removes the reparse points under the root before the recursive delete, so cleanup never
    /// descends through a planted junction into its target.</summary>
    public void Dispose()
    {
        try
        {
            RemoveReparsePoints(Root);
            Directory.Delete(Root, true);
        }
        catch (IOException)
        {
        }
    }

    private static void RemoveReparsePoints(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    Directory.Delete(entry, false);
                }
                else
                {
                    File.Delete(entry);
                }

                continue;
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                RemoveReparsePoints(entry);
            }
        }
    }
}
