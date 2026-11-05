using System.Globalization;
using System.Security.Cryptography;

namespace Ursacopy.Core;

/// <summary>
/// Incremental one-way copy of a folder tree. A file is copied when it is missing from the target or differs in size or
/// modification time; it is written under a temporary name then renamed, so an interrupted run never leaves a truncated file.
/// Deleted and replaced files go to <c>.ursacopy-trash</c> for a configurable number of days.
/// </summary>
public sealed class CopyEngine
{
    public const string PartSuffix = ".ursacopy-part";
    public const string TrashDirName = ".ursacopy-trash";
    const string StampFormat = "yyyyMMdd-HHmmss";
    const int BufferSize = 1 << 20;
    static readonly double ToleranceSeconds = 2; // FAT / exFAT keep modification times with a 2 s granularity

    readonly CopyOptions _o;
    readonly CopyProgress _p;
    readonly CancellationToken _ct;
    readonly ExcludeMatcher _exclude;
    readonly List<string> _errors = [];
    readonly string _srcRoot, _dstRoot, _trashRoot, _stamp;
    readonly List<string> _inc;

    CopyEngine(CopyOptions o, CopyProgress p, CancellationToken ct)
    {
        _o = o; _p = p; _ct = ct;
        _inc = o.Includes.Select(i => i.Trim().Replace('\\', '/').Trim('/')).Where(i => i.Length > 0).Distinct().ToList();
        _exclude = new ExcludeMatcher(o.Excludes);
        _srcRoot = Path.GetFullPath(o.SourceDir);
        var target = Path.GetFullPath(o.TargetDir);
        _dstRoot = Path.Combine(target, o.DestFolder);
        _trashRoot = Path.Combine(target, TrashDirName, o.DestFolder);
        _stamp = DateTime.UtcNow.ToString(StampFormat, CultureInfo.InvariantCulture);
    }

    public static CopyResult Run(CopyOptions options, CopyProgress progress, CancellationToken ct) =>
        new CopyEngine(options, progress, ct).Execute();

    CopyResult Execute()
    {
        var started = DateTime.UtcNow;
        CopyStatus status;
        string? message = null;
        try
        {
            Validate();
            _p.Phase = "Copying";
            WalkDir(_srcRoot, _dstRoot, "");
            _p.Phase = "Cleaning the trash";
            PurgeTrash();
            status = _p.Errors > 0 ? CopyStatus.CompletedWithErrors : CopyStatus.Success;
            if (status == CopyStatus.CompletedWithErrors) message = $"{_p.Errors} item(s) could not be copied.";
        }
        catch (OperationCanceledException) { status = CopyStatus.Cancelled; message = "Cancelled."; }
        catch (CopyAbortedException ex) { status = CopyStatus.Failed; message = ex.Message; }
        catch (Exception ex) { status = CopyStatus.Failed; message = ex.Message; }
        _p.Phase = "Finished";
        _p.CurrentFile = null;
        return new CopyResult(status, message, _errors, DateTime.UtcNow - started);
    }

    void Validate()
    {
        var name = _o.DestFolder;
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(['/', '\\']) >= 0 || name.StartsWith('.'))
            throw new CopyAbortedException("The destination folder must be a plain folder name (no slash, not starting with a dot).");
        if (_inc.Any(i => i.Split('/').Any(s => s is "." or "..") || Path.IsPathRooted(i)))
            throw new CopyAbortedException("The selected folders must be paths inside the source folder.");
        if (!Directory.Exists(_srcRoot))
            throw new CopyAbortedException($"The source folder '{_o.SourceDir}' does not exist (is the disk mounted in the container?).");
        var target = Path.GetFullPath(_o.TargetDir);
        if (!Directory.Exists(target))
            throw new CopyAbortedException($"The target folder '{_o.TargetDir}' does not exist.");
        var marker = TargetMarker.Read(target);
        if (marker is null)
            throw new CopyAbortedException("The external disk is not connected or not initialised (no .ursacopy-target marker at its root). Nothing was written.");
        if (!string.Equals(marker, _o.TargetId, StringComparison.Ordinal))
            throw new CopyAbortedException("The disk found at the target is not the one this job was set up with (different marker). Nothing was written.");
        if (IsInside(target, _srcRoot) || IsInside(_srcRoot, target))
            throw new CopyAbortedException("The source and the target folders must not contain each other.");
        // A missing or empty source (unmounted share, wrong path) would look like "everything was deleted": refuse when the copy holds data.
        bool srcEmpty = !Directory.EnumerateFileSystemEntries(_srcRoot).Any();
        if (srcEmpty && Directory.Exists(_dstRoot) && Directory.EnumerateFileSystemEntries(_dstRoot).Any())
            throw new CopyAbortedException("The source folder is empty while the copy already holds data: it is probably not mounted. Nothing was changed.");
        Directory.CreateDirectory(_dstRoot);
    }

    static bool IsInside(string child, string parent)
    {
        var p = Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar;
        return (child + Path.DirectorySeparatorChar).StartsWith(p, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    void WalkDir(string src, string dst, string rel)
    {
        _ct.ThrowIfCancellationRequested();
        List<FileSystemInfo> entries;
        try
        {
            entries = new DirectoryInfo(src).EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = false, AttributesToSkip = 0 }).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without the list of the source we cannot tell what is gone: leave this folder of the copy untouched.
            Error(rel.Length == 0 ? "/" : rel, ex);
            return;
        }

        var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            _ct.ThrowIfCancellationRequested();
            var childRel = rel.Length == 0 ? e.Name : rel + "/" + e.Name;
            if (_exclude.IsExcluded(childRel, e.Name)) continue;
            var sel = Select(childRel);
            if (sel == Sel.None || (sel == Sel.Partial && e is not DirectoryInfo)) continue; // outside the chosen folders: not kept, so removed from the copy
            if ((e.Attributes & FileAttributes.ReparsePoint) != 0) { _p.Skipped++; continue; } // symbolic links are not followed
            kept.Add(e.Name);
            var childDst = Path.Combine(dst, e.Name);
            try
            {
                if (e is DirectoryInfo d)
                {
                    if (File.Exists(childDst)) Discard(childDst, childRel, replaced: true);
                    Directory.CreateDirectory(childDst);
                    WalkDir(d.FullName, childDst, childRel);
                }
                else CopyFile((FileInfo)e, childDst, childRel);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not CopyAbortedException)
            {
                Error(childRel, ex);
            }
        }

        // What the copy holds and the source no longer has (or has but excluded), plus leftovers of an interrupted run.
        try
        {
            foreach (var d in new DirectoryInfo(dst).EnumerateFileSystemInfos().ToList())
            {
                _ct.ThrowIfCancellationRequested();
                var childRel = rel.Length == 0 ? d.Name : rel + "/" + d.Name;
                try
                {
                    if (d is FileInfo && d.Name.EndsWith(PartSuffix, StringComparison.Ordinal)) { d.Delete(); continue; }
                    if (_o.MirrorDeletes && !kept.Contains(d.Name)) Discard(d.FullName, childRel, replaced: false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not CopyAbortedException) { Error(childRel, ex); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Error(rel.Length == 0 ? "/" : rel, ex); }
    }

    enum Sel { None, Partial, Full }

    /// <summary>Full: at or inside a chosen folder. Partial: a folder that leads to a chosen one. None: not wanted.</summary>
    Sel Select(string rel)
    {
        if (_inc.Count == 0) return Sel.Full;
        bool partial = false;
        foreach (var i in _inc)
        {
            if (rel == i || rel.StartsWith(i + "/", StringComparison.Ordinal)) return Sel.Full;
            if (i.StartsWith(rel + "/", StringComparison.Ordinal)) partial = true;
        }
        return partial ? Sel.Partial : Sel.None;
    }

    void CopyFile(FileInfo src, string dst, string rel)
    {
        _p.FilesSeen++;
        _p.BytesSeen += src.Length;
        bool existing = false;
        if (File.Exists(dst))
        {
            var cur = new FileInfo(dst);
            if (cur.Length == src.Length && Math.Abs((cur.LastWriteTimeUtc - src.LastWriteTimeUtc).TotalSeconds) <= ToleranceSeconds)
            {
                _p.Unchanged++;
                return;
            }
            existing = true;
        }
        else if (Directory.Exists(dst)) Discard(dst, rel, replaced: true);

        _p.CurrentFile = rel;
        _p.CurrentFileSize = src.Length;
        var tmp = dst + PartSuffix;
        try
        {
            byte[]? sourceHash = null;
            using (var input = new FileStream(src.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, BufferSize, FileOptions.SequentialScan))
            using (var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.SequentialScan))
            using (var hash = _o.Verify ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null)
            {
                var buf = new byte[BufferSize];
                int n;
                while ((n = input.Read(buf, 0, buf.Length)) > 0)
                {
                    _ct.ThrowIfCancellationRequested();
                    hash?.AppendData(buf, 0, n);
                    output.Write(buf, 0, n);
                    _p.BytesCopied += n;
                }
                output.Flush(true); // data on the disk before the rename: a USB disk may be unplugged at any time
                sourceHash = hash?.GetHashAndReset();
            }
            File.SetLastWriteTimeUtc(tmp, src.LastWriteTimeUtc);
            if (sourceHash is not null && !sourceHash.AsSpan().SequenceEqual(HashFile(tmp)))
                throw new IOException("Verification failed: the copy does not match the source.");
            if (existing && _o.TrashRetentionDays > 0) Discard(dst, rel, replaced: true);
            File.Move(tmp, dst, overwrite: true);
            _p.Copied++;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            try { File.Delete(tmp); } catch { /* best effort */ }
            if (ex is IOException && IsDiskFull(src.Length))
                throw new CopyAbortedException("The external disk is full.");
            throw;
        }
        catch (OperationCanceledException)
        {
            try { File.Delete(tmp); } catch { /* best effort */ }
            throw;
        }
    }

    bool IsDiskFull(long needed)
    {
        try { return new DriveInfo(_dstRoot).AvailableFreeSpace < needed; }
        catch { return false; }
    }

    static byte[] HashFile(string path)
    {
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
        return SHA256.HashData(s);
    }

    /// <summary>Removes a file or folder from the copy: into the dated trash when a retention is set, otherwise for good.</summary>
    void Discard(string path, string rel, bool replaced)
    {
        bool isDir = Directory.Exists(path);
        if (_o.TrashRetentionDays > 0)
        {
            var dest = Path.Combine(_trashRoot, _stamp, rel.Replace('/', Path.DirectorySeparatorChar));
            for (int i = 1; File.Exists(dest) || Directory.Exists(dest); i++) dest = $"{dest}.{i}";
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            if (isDir) Directory.Move(path, dest); else File.Move(path, dest);
        }
        else if (isDir) { ClearReadOnly(path); Directory.Delete(path, recursive: true); }
        else { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); }
        if (!replaced) _p.Deleted++;
    }

    static void ClearReadOnly(string dir)
    {
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            try { File.SetAttributes(f, FileAttributes.Normal); } catch { /* best effort */ }
    }

    void PurgeTrash()
    {
        if (!Directory.Exists(_trashRoot)) return;
        var limit = DateTime.UtcNow.AddDays(-_o.TrashRetentionDays);
        foreach (var dir in Directory.EnumerateDirectories(_trashRoot))
        {
            if (!DateTime.TryParseExact(Path.GetFileName(dir), StampFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var when)) continue;
            if (when >= limit && _o.TrashRetentionDays > 0) continue;
            try { ClearReadOnly(dir); Directory.Delete(dir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Error("trash/" + Path.GetFileName(dir), ex); }
        }
    }

    void Error(string rel, Exception ex)
    {
        _p.Errors++;
        if (_errors.Count < 200) _errors.Add($"{rel}: {ex.Message}");
    }
}
