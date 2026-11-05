using Ursacopy.Core;
using Xunit;

namespace Ursacopy.Tests;

public sealed class CopyEngineTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "ursacopy-" + Guid.NewGuid().ToString("N"));
    readonly string _src, _usb;
    readonly string _id;

    public CopyEngineTests()
    {
        _src = Path.Combine(_root, "src");
        _usb = Path.Combine(_root, "usb");
        Directory.CreateDirectory(_src);
        Directory.CreateDirectory(_usb);
        _id = TargetMarker.Initialize(_usb);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    CopyOptions Opts(int retention = 30, bool mirror = true, bool verify = false, string[]? ex = null, string? id = null) => new()
    {
        SourceDir = _src, TargetDir = _usb, TargetId = id ?? _id, DestFolder = "data",
        MirrorDeletes = mirror, TrashRetentionDays = retention, Verify = verify, Excludes = ex ?? [],
    };

    CopyResult Run(CopyOptions o, out CopyProgress p) { p = new CopyProgress(); return CopyEngine.Run(o, p, CancellationToken.None); }
    void Write(string rel, string text, DateTime? mtime = null)
    {
        var f = Path.Combine(_src, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.WriteAllText(f, text);
        if (mtime is { } m) File.SetLastWriteTimeUtc(f, m);
    }
    string Dst(string rel) => Path.Combine(_usb, "data", rel.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void FirstRunCopiesEverythingAndSecondRunCopiesNothing()
    {
        Write("a.txt", "A"); Write("sub/b.txt", "BB"); Write("sub/deep/c.txt", "CCC");
        var r1 = Run(Opts(verify: true), out var p1);
        Assert.Equal(CopyStatus.Success, r1.Status);
        Assert.Equal(3, p1.Copied);
        Assert.Equal("CCC", File.ReadAllText(Dst("sub/deep/c.txt")));
        Assert.Equal(File.GetLastWriteTimeUtc(Path.Combine(_src, "a.txt")), File.GetLastWriteTimeUtc(Dst("a.txt")));

        var r2 = Run(Opts(), out var p2);
        Assert.Equal(CopyStatus.Success, r2.Status);
        Assert.Equal(0, p2.Copied);
        Assert.Equal(3, p2.Unchanged);
    }

    [Fact]
    public void ChangedFileIsRecopiedAndOldVersionKeptInTrash()
    {
        Write("a.txt", "one", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Run(Opts(), out _);
        Write("a.txt", "two!", new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var r = Run(Opts(), out var p);
        Assert.Equal(1, p.Copied);
        Assert.Equal("two!", File.ReadAllText(Dst("a.txt")));
        var old = Directory.GetFiles(Path.Combine(_usb, CopyEngine.TrashDirName), "a.txt", SearchOption.AllDirectories);
        Assert.Single(old);
        Assert.Equal("one", File.ReadAllText(old[0]));
        Assert.Equal(0, p.Deleted); // a replaced file is not a deletion
    }

    [Fact]
    public void DeletedSourceFileMovesToTrashWhenMirroring_AndStaysWhenNot()
    {
        Write("keep.txt", "k"); Write("gone/x.txt", "x");
        Run(Opts(), out _);
        Directory.Delete(Path.Combine(_src, "gone"), true);

        Run(Opts(mirror: false), out var pNo);
        Assert.True(File.Exists(Dst("gone/x.txt")));
        Assert.Equal(0, pNo.Deleted);

        Run(Opts(), out var p);
        Assert.False(Directory.Exists(Dst("gone")));
        Assert.Equal(1, p.Deleted);
        Assert.Single(Directory.GetFiles(Path.Combine(_usb, CopyEngine.TrashDirName), "x.txt", SearchOption.AllDirectories));
    }

    [Fact]
    public void ZeroRetentionDeletesForGood()
    {
        Write("a.txt", "a"); Write("b.txt", "b");
        Run(Opts(retention: 0), out _);
        File.Delete(Path.Combine(_src, "b.txt"));
        Run(Opts(retention: 0), out _);
        Assert.False(File.Exists(Dst("b.txt")));
        Assert.False(Directory.Exists(Path.Combine(_usb, CopyEngine.TrashDirName)) && Directory.GetFiles(Path.Combine(_usb, CopyEngine.TrashDirName), "*", SearchOption.AllDirectories).Length > 0);
    }

    [Fact]
    public void OldTrashIsPurged()
    {
        Write("a.txt", "a");
        Run(Opts(), out _);
        var old = Path.Combine(_usb, CopyEngine.TrashDirName, "data", DateTime.UtcNow.AddDays(-40).ToString("yyyyMMdd-HHmmss"));
        var recent = Path.Combine(_usb, CopyEngine.TrashDirName, "data", DateTime.UtcNow.AddDays(-5).ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(old); Directory.CreateDirectory(recent);
        File.WriteAllText(Path.Combine(old, "f"), "x"); File.WriteAllText(Path.Combine(recent, "f"), "x");
        Run(Opts(retention: 30), out _);
        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(recent));
    }

    [Fact]
    public void MissingOrWrongMarkerWritesNothing()
    {
        Write("a.txt", "a");
        File.Delete(Path.Combine(_usb, TargetMarker.FileName));
        var r = Run(Opts(), out _);
        Assert.Equal(CopyStatus.Failed, r.Status);
        Assert.False(Directory.Exists(Path.Combine(_usb, "data")));

        File.WriteAllText(Path.Combine(_usb, TargetMarker.FileName), "other-disk");
        r = Run(Opts(), out _);
        Assert.Equal(CopyStatus.Failed, r.Status);
        Assert.False(Directory.Exists(Path.Combine(_usb, "data")));
    }

    [Fact]
    public void EmptySourceDoesNotWipeANonEmptyCopy()
    {
        Write("a.txt", "a");
        Run(Opts(), out _);
        File.Delete(Path.Combine(_src, "a.txt"));
        var r = Run(Opts(), out _);
        Assert.Equal(CopyStatus.Failed, r.Status);
        Assert.True(File.Exists(Dst("a.txt")));
    }

    [Fact]
    public void ExcludesAreNotCopiedAndAreRemovedFromTheCopy()
    {
        Write("a.txt", "a"); Write("@eaDir/x", "x"); Write("docs/tmp.tmp", "t"); Write("docs/keep.txt", "k");
        Run(Opts(ex: ["@eaDir", "*.tmp"]), out var p);
        Assert.Equal(2, p.Copied);
        Assert.False(Directory.Exists(Dst("@eaDir")));
        Assert.False(File.Exists(Dst("docs/tmp.tmp")));
        Assert.True(File.Exists(Dst("docs/keep.txt")));
    }

    [Fact]
    public void OnlyChosenFoldersAreCopied_AndUnchosenOnesAreRemovedLater()
    {
        Write("root.txt", "r"); Write("photos/a.jpg", "a"); Write("docs/2024/x.txt", "x"); Write("docs/2023/y.txt", "y"); Write("video/v.mp4", "v");
        CopyOptions Inc(params string[] i) => new()
        {
            SourceDir = _src, TargetDir = _usb, TargetId = _id, DestFolder = "data", Includes = i,
        };
        var r = Run(Inc("photos", "docs/2024"), out var p);
        Assert.Equal(CopyStatus.Success, r.Status);
        Assert.Equal(2, p.Copied);
        Assert.True(File.Exists(Dst("photos/a.jpg")));
        Assert.True(File.Exists(Dst("docs/2024/x.txt")));
        Assert.False(File.Exists(Dst("docs/2023/y.txt")));
        Assert.False(File.Exists(Dst("root.txt")));
        Assert.False(Directory.Exists(Dst("video")));

        Run(Inc("photos"), out var p2); // docs/2024 unchecked: leaves the copy, into the trash
        Assert.False(Directory.Exists(Dst("docs")));
        Assert.True(File.Exists(Dst("photos/a.jpg")));
        Assert.Single(Directory.GetFiles(Path.Combine(_usb, CopyEngine.TrashDirName), "x.txt", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("../x")] [InlineData("a/../b")]
    public void IncludesOutsideTheSourceAreRefused(string inc)
    {
        Write("a.txt", "a");
        var o = new CopyOptions { SourceDir = _src, TargetDir = _usb, TargetId = _id, DestFolder = "data", Includes = [inc] };
        Assert.Equal(CopyStatus.Failed, CopyEngine.Run(o, new CopyProgress(), CancellationToken.None).Status);
    }

    [Fact]
    public void LeftoverPartFilesAreCleaned()
    {
        Write("a.txt", "a");
        Run(Opts(), out _);
        File.WriteAllText(Dst("a.txt") + CopyEngine.PartSuffix, "junk");
        Run(Opts(), out _);
        Assert.False(File.Exists(Dst("a.txt") + CopyEngine.PartSuffix));
    }

    [Fact]
    public void CancellationStopsTheRunAndLeavesNoPartFile()
    {
        for (int i = 0; i < 50; i++) Write($"f{i}.txt", new string('x', 1000));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var r = CopyEngine.Run(Opts(), new CopyProgress(), cts.Token);
        Assert.Equal(CopyStatus.Cancelled, r.Status);
        Assert.Empty(Directory.GetFiles(_usb, "*" + CopyEngine.PartSuffix, SearchOption.AllDirectories));
    }

    [Fact]
    public void SourceInsideTargetIsRefused()
    {
        var o = new CopyOptions { SourceDir = Path.Combine(_usb, "data"), TargetDir = _usb, TargetId = _id, DestFolder = "data" };
        Directory.CreateDirectory(o.SourceDir);
        File.WriteAllText(Path.Combine(o.SourceDir, "f"), "x");
        Assert.Equal(CopyStatus.Failed, CopyEngine.Run(o, new CopyProgress(), CancellationToken.None).Status);
    }

    [Theory]
    [InlineData("..")] [InlineData("a/b")] [InlineData(".hidden")] [InlineData("")]
    public void BadDestFolderIsRefused(string name)
    {
        Write("a.txt", "a");
        var o = new CopyOptions { SourceDir = _src, TargetDir = _usb, TargetId = _id, DestFolder = name };
        Assert.Equal(CopyStatus.Failed, CopyEngine.Run(o, new CopyProgress(), CancellationToken.None).Status);
    }

    [Theory]
    [InlineData("*.tmp", "a/b.tmp", "b.tmp", true)]
    [InlineData("*.tmp", "a/b.txt", "b.txt", false)]
    [InlineData("cache/", "x/cache", "cache", true)]
    [InlineData("x/cache", "x/cache", "cache", true)]
    [InlineData("x/cache", "y/cache", "cache", false)]
    [InlineData("THUMBS.DB", "p/thumbs.db", "thumbs.db", true)]
    public void ExcludeMatcherRules(string pattern, string rel, string name, bool expected) =>
        Assert.Equal(expected, new ExcludeMatcher([pattern]).IsExcluded(rel, name));
}
