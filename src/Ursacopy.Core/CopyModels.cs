namespace Ursacopy.Core;

/// <summary>Settings of one copy run (one source folder mirrored into one folder of the external disk).</summary>
public sealed class CopyOptions
{
    /// <summary>Folder to copy (mounted read-only in the container).</summary>
    public required string SourceDir { get; init; }
    /// <summary>Root of the external disk (where the <c>.ursacopy-target</c> marker lives).</summary>
    public required string TargetDir { get; init; }
    /// <summary>Id the marker file of the external disk must contain; protects against writing into an empty mount point.</summary>
    public required string TargetId { get; init; }
    /// <summary>Single folder name, under <see cref="TargetDir"/>, that receives the mirror.</summary>
    public required string DestFolder { get; init; }
    /// <summary>Remove from the copy what no longer exists in the source.</summary>
    public bool MirrorDeletes { get; init; } = true;
    /// <summary>Days to keep deleted and replaced files in <c>.ursacopy-trash</c> (0 = delete / overwrite right away).</summary>
    public int TrashRetentionDays { get; init; } = 30;
    /// <summary>Re-read each copied file and compare its SHA-256 with the one of the data read from the source.</summary>
    public bool Verify { get; init; }
    /// <summary>Wildcard patterns (<c>*</c>, <c>?</c>) matched against the name, or against the relative path when they contain a slash.</summary>
    public IReadOnlyList<string> Excludes { get; init; } = [];
}

/// <summary>Live counters of a run; written by the engine, read by the UI (a torn read of a counter is harmless).</summary>
public sealed class CopyProgress
{
    public volatile string Phase = "Starting";
    public volatile string? CurrentFile;
    public long CurrentFileSize;
    public long FilesSeen;
    public long BytesSeen;
    public long Copied;
    public long BytesCopied;
    public long Unchanged;
    public long Deleted;
    public long Skipped;
    public long Errors;
}

public enum CopyStatus { Success, CompletedWithErrors, Failed, Cancelled }

public sealed record CopyResult(CopyStatus Status, string? Message, IReadOnlyList<string> ErrorSamples, TimeSpan Elapsed);

/// <summary>A condition that makes the whole run stop (as opposed to an error on one file).</summary>
public sealed class CopyAbortedException(string message) : Exception(message);
