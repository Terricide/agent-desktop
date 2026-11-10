using System.Diagnostics;
using AgentDesktop.Core.Response;
using AgentDesktop.Windows;
using Xunit;

namespace AgentDesktop.Windows.Tests;

public sealed class PrivateFilePathTests
{
    private static uint AttributesOf(string path) => (uint)File.GetAttributes(path);

    private static void RefuseChainUpToParent(string destination)
    {
        var parent = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("destination must have a parent");
        PrivateFilePath.RequireVerifiedDirectoryChain(parent, AttributesOf);
    }

    private static void CreateJunction(string link, string target)
    {
        var info = new ProcessStartInfo("cmd", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        var process = Process.Start(info)
            ?? throw new InvalidOperationException("cmd /c mklink /J must spawn");
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, "junction creation must succeed");
        Assert.True(
            (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0,
            "the planted link must surface as a reparse point");
    }

    [Fact]
    public void AJunctionComponentOnTheWritePathIsRefusedAndNothingLandsAtItsTarget()
    {
        using var root = Scratch.New("junction-mid");
        using var elsewhere = Scratch.New("junction-elsewhere");
        var junction = Path.Combine(root.Root, "redirect");
        CreateJunction(junction, elsewhere.Root);
        var destination = Path.Combine(junction, "nested", "artifact.json");

        var refusal = Assert.Throws<AdapterError>(() => RefuseChainUpToParent(destination));

        Assert.Equal(ErrorCode.PermDenied, refusal.Code);
        Assert.Contains("reparse point", refusal.Message);
        Assert.False(
            File.Exists(Path.Combine(elsewhere.Root, "nested", "artifact.json")),
            "no artifact may land at the junction target");
        Assert.False(
            Directory.Exists(Path.Combine(elsewhere.Root, "nested")),
            "no directory may be created at the junction target");
    }

    [Fact]
    public void AJunctionAsTheImmediateParentIsRefusedAndNothingLandsAtItsTarget()
    {
        using var root = Scratch.New("junction-parent");
        using var elsewhere = Scratch.New("junction-parent-elsewhere");
        var junction = Path.Combine(root.Root, "redirect");
        CreateJunction(junction, elsewhere.Root);
        var destination = Path.Combine(junction, "artifact.json");

        var refusal = Assert.Throws<AdapterError>(() => RefuseChainUpToParent(destination));

        Assert.Equal(ErrorCode.PermDenied, refusal.Code);
        Assert.False(
            File.Exists(Path.Combine(elsewhere.Root, "artifact.json")),
            "no artifact may land at the junction target");
    }

    [Fact]
    public void AJunctionLeafIsRefusedForPrivateOpens()
    {
        using var root = Scratch.New("junction-leaf");
        using var elsewhere = Scratch.New("junction-leaf-elsewhere");
        var junction = Path.Combine(root.Root, "redirect");
        CreateJunction(junction, elsewhere.Root);

        var appendRefusal = Assert.Throws<AdapterError>(
            () => PrivateFilePath.RequireRegularLeaf(AttributesOf(junction), "private append target"));
        var lockRefusal = Assert.Throws<AdapterError>(
            () => PrivateFilePath.RequireRegularLeaf(AttributesOf(junction), "private lock file"));
        var readRefusal = Assert.Throws<AdapterError>(
            () => PrivateFilePath.RequireRegularLeaf(AttributesOf(junction), "private file"));

        Assert.Contains("is a reparse point", appendRefusal.Message);
        Assert.Contains("is a reparse point", lockRefusal.Message);
        Assert.Contains("is a reparse point", readRefusal.Message);
    }

    [Fact]
    public void ARegularFileWhereADirectoryIsExpectedIsRefused()
    {
        using var root = Scratch.New("file-component");
        var blockingFile = Path.Combine(root.Root, "blocking.txt");
        File.WriteAllText(blockingFile, "a file, not a directory");
        var destination = Path.Combine(blockingFile, "artifact.json");

        var refusal = Assert.Throws<AdapterError>(() => RefuseChainUpToParent(destination));

        Assert.Equal(ErrorCode.PermDenied, refusal.Code);
        Assert.Equal("a file, not a directory", File.ReadAllText(blockingFile));
    }

    [Fact]
    public void ADirectoryDestinationIsRefusedForAtomicWrites()
    {
        using var root = Scratch.New("dir-destination");
        var destination = Path.Combine(root.Root, "already-a-directory");
        Directory.CreateDirectory(destination);

        var refusal = Assert.Throws<AdapterError>(
            () => PrivateFilePath.RequireRegularLeaf(AttributesOf(destination), "private file destination"));

        Assert.Equal(ErrorCode.PermDenied, refusal.Code);
        Assert.True(Directory.Exists(destination), "the directory must be left untouched");
    }

    [Fact]
    public void ParentTraversalComponentsAreRejected()
    {
        using var root = Scratch.New("traversal");
        var sneaky = Path.Combine(root.Root, "..", "outside", "artifact.json");

        var refusal = Assert.Throws<AdapterError>(() => RefuseChainUpToParent(sneaky));

        Assert.Equal(ErrorCode.InvalidArgs, refusal.Code);
        Assert.Contains("parent traversal", refusal.Message);
    }

    [Fact]
    public void ALeadingCurrentDirComponentIsSkippedNotRejected()
    {
        var prefixes = PrivateFilePath.PinnedPrefixes(Path.Combine(".", "sub", "artifact.json"));

        Assert.DoesNotContain(".", prefixes);
        Assert.Equal("sub", prefixes[0]);
        Assert.Equal(Path.Combine("sub", "artifact.json"), prefixes[^1]);
    }

    [Fact]
    public void ANestedChainWalksRootFirstAndEndsAtTheParent()
    {
        using var root = Scratch.New("ensure-nested");
        var nested = Path.Combine(root.Root, "sessions", "s1", "trace");
        Directory.CreateDirectory(nested);
        var parent = Path.Combine(nested, "segment.jsonl");

        var prefixes = PrivateFilePath.PinnedPrefixes(Path.GetDirectoryName(parent)!);

        Assert.Equal(Path.GetPathRoot(nested), prefixes[0]);
        Assert.Equal(nested, prefixes[^1]);
        PrivateFilePath.RequireVerifiedDirectoryChain(nested, AttributesOf);
        Assert.True(Directory.Exists(nested));
    }

    [Fact]
    public void AReparsePointComponentIsRefusedBeforeTheDirectoryCheck()
    {
        using var root = Scratch.New("reparse-order");
        using var elsewhere = Scratch.New("reparse-order-target");
        var junction = Path.Combine(root.Root, "redirect");
        CreateJunction(junction, elsewhere.Root);

        var refusal = Assert.Throws<AdapterError>(
            () => PrivateFilePath.RequireVerifiedDirectory(AttributesOf(junction)));

        Assert.Contains("reparse point", refusal.Message);
    }

    [Fact(Skip = "requires the private-file write seam, the temp lease, and the no-follow handle open")]
    public void WriteReadRoundtripPreservesBytesAndEnforcesReadLimits()
    {
    }

    [Fact(Skip = "requires the private-file write seam and ReplaceFileW promotion")]
    public void OverwritingAnExistingDestinationReplacesItsContent()
    {
    }

    [Fact(Skip = "requires the private append open and the owner, dacl, and locality gates")]
    public void AppendOpensCreateThenGrowsTheFileWithAReadableHandle()
    {
    }

    [Fact(Skip = "requires the private lock open and the owner, dacl, and locality gates")]
    public void LockOpensHonorTheCreateFlag()
    {
    }

    [Fact(Skip = "requires the no-follow pin handle, which the interop layer does not declare")]
    public void APinnedDirectoryCannotBeRenamedWhileItsNoFollowHandleIsHeld()
    {
    }

    [Fact(Skip = "requires ensure_private, which needs the dacl and owner gates")]
    public void EnsurePrivateCreatesANestedChainThatAcceptsWrites()
    {
    }

    [Fact(Skip = "requires the dacl protection of a created private leaf")]
    public void ANewPrivateLeafDoesNotInheritABroadUsersGrant()
    {
    }

    [Fact(Skip = "requires the dacl check of an existing private leaf")]
    public void AnExistingPrivateLeafWithAUsersGrantIsRefusedWithoutRepair()
    {
    }
}
