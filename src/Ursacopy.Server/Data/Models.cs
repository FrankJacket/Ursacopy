namespace Ursacopy.Server.Data;

public static class Schedule
{
    public const int Manual = 0, Daily = 1, OnPlug = 2;
}

/// <summary>One folder mirrored into one folder of an external disk.</summary>
public sealed class Job
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string SourceDir { get; set; } = "";
    /// <summary>Mount point of the external disk in the container.</summary>
    public string TargetDir { get; set; } = "/target";
    /// <summary>Folder of the external disk that receives the mirror.</summary>
    public string DestFolder { get; set; } = "";
    /// <summary>Id of the marker of the external disk this job was set up with; null until the disk is initialised.</summary>
    public string? TargetId { get; set; }
    public bool Enabled { get; set; } = true;
    public int ScheduleMode { get; set; } = Schedule.Manual;
    public string DailyTime { get; set; } = "02:00";
    public bool MirrorDeletes { get; set; } = true;
    public int RetentionDays { get; set; } = 30;
    public bool Verify { get; set; }
    public string Excludes { get; set; } = string.Join('\n', Ursacopy.Core.ExcludeMatcher.NasDefaults);
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Number of files seen by the last complete run: the denominator of the progress bar.</summary>
    public long EstimatedFiles { get; set; }

    public string[] ExcludeList() =>
        Excludes.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public sealed class RunRow
{
    public int Id { get; set; }
    public int JobId { get; set; }
    public string JobName { get; set; } = "";
    public DateTime StartedUtc { get; set; }
    public DateTime? EndedUtc { get; set; }
    /// <summary>Running, Success, CompletedWithErrors, Failed, Cancelled or Interrupted.</summary>
    public string Status { get; set; } = "Running";
    public string Reason { get; set; } = "";
    public long FilesSeen { get; set; }
    public long Copied { get; set; }
    public long BytesCopied { get; set; }
    public long Unchanged { get; set; }
    public long Deleted { get; set; }
    public long Errors { get; set; }
    public string? Message { get; set; }
    public string? Details { get; set; }
}
