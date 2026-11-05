using Dapper;

namespace Ursacopy.Server.Data;

public sealed class Repo(Db db)
{
    const string JobCols = "Id, Name, SourceDir, Includes, TargetDir, DestFolder, TargetId, Enabled, ScheduleMode, DailyTime, MirrorDeletes, RetentionDays, Verify, WindowsDisk, Excludes, CreatedUtc, EstimatedFiles, EstimatedBytes";

    public List<Job> ListJobs()
    {
        using var c = db.Open();
        return c.Query<Job>($"SELECT {JobCols} FROM Jobs ORDER BY Name COLLATE NOCASE").ToList();
    }

    public Job? GetJob(int id)
    {
        using var c = db.Open();
        return c.QueryFirstOrDefault<Job>($"SELECT {JobCols} FROM Jobs WHERE Id=@id", new { id });
    }

    public int SaveJob(Job j)
    {
        using var c = db.Open();
        if (j.Id == 0)
            return j.Id = c.ExecuteScalar<int>("""
                INSERT INTO Jobs (Name, SourceDir, Includes, TargetDir, DestFolder, TargetId, Enabled, ScheduleMode, DailyTime, MirrorDeletes, RetentionDays, Verify, WindowsDisk, Excludes, CreatedUtc, EstimatedFiles, EstimatedBytes)
                VALUES (@Name, @SourceDir, @Includes, @TargetDir, @DestFolder, @TargetId, @Enabled, @ScheduleMode, @DailyTime, @MirrorDeletes, @RetentionDays, @Verify, @WindowsDisk, @Excludes, @CreatedUtc, @EstimatedFiles, @EstimatedBytes);
                SELECT last_insert_rowid();
                """, j);
        c.Execute("""
            UPDATE Jobs SET Name=@Name, SourceDir=@SourceDir, Includes=@Includes, TargetDir=@TargetDir, DestFolder=@DestFolder, TargetId=@TargetId, Enabled=@Enabled,
              ScheduleMode=@ScheduleMode, DailyTime=@DailyTime, MirrorDeletes=@MirrorDeletes, RetentionDays=@RetentionDays, Verify=@Verify, WindowsDisk=@WindowsDisk, Excludes=@Excludes
            WHERE Id=@Id
            """, j);
        return j.Id;
    }

    public void SetTargetId(int jobId, string targetId)
    {
        using var c = db.Open();
        c.Execute("UPDATE Jobs SET TargetId=@targetId WHERE Id=@jobId", new { jobId, targetId });
    }

    public void SetEstimate(int jobId, long files, long bytes)
    {
        using var c = db.Open();
        c.Execute("UPDATE Jobs SET EstimatedFiles=@files, EstimatedBytes=@bytes WHERE Id=@jobId", new { jobId, files, bytes });
    }

    /// <summary>Deletes the job and its history; the copy on the external disk is left alone.</summary>
    public void DeleteJob(int id)
    {
        using var c = db.Open();
        c.Execute("DELETE FROM Runs WHERE JobId=@id; DELETE FROM Jobs WHERE Id=@id", new { id });
    }

    public int AddRun(Job j, string reason)
    {
        using var c = db.Open();
        return c.ExecuteScalar<int>("""
            INSERT INTO Runs (JobId, JobName, StartedUtc, Status, Reason) VALUES (@Id, @Name, @now, 'Running', @reason);
            SELECT last_insert_rowid();
            """, new { j.Id, j.Name, now = DateTime.UtcNow, reason });
    }

    public void FinishRun(RunRow r)
    {
        using var c = db.Open();
        c.Execute("""
            UPDATE Runs SET EndedUtc=@EndedUtc, Status=@Status, FilesSeen=@FilesSeen, Copied=@Copied, BytesCopied=@BytesCopied,
              Unchanged=@Unchanged, Deleted=@Deleted, Errors=@Errors, Message=@Message, Details=@Details WHERE Id=@Id
            """, r);
    }

    /// <summary>After a crash or a restart of the container, runs still marked "Running" were cut short.</summary>
    public void MarkInterruptedRuns()
    {
        using var c = db.Open();
        c.Execute("UPDATE Runs SET Status='Interrupted', EndedUtc=@now, Message='The server stopped during this run.' WHERE Status='Running'", new { now = DateTime.UtcNow });
    }

    public List<RunRow> ListRuns(int limit = 100, int? jobId = null)
    {
        using var c = db.Open();
        return c.Query<RunRow>("SELECT * FROM Runs WHERE (@jobId IS NULL OR JobId=@jobId) ORDER BY Id DESC LIMIT @limit", new { jobId, limit }).ToList();
    }

    /// <summary>Most recent run of each job, keyed by job id.</summary>
    public Dictionary<int, RunRow> LastRuns()
    {
        using var c = db.Open();
        return c.Query<RunRow>("SELECT * FROM Runs WHERE Id IN (SELECT MAX(Id) FROM Runs GROUP BY JobId)").ToDictionary(r => r.JobId);
    }

    public DateTime? LastStartedUtc(int jobId)
    {
        using var c = db.Open();
        return c.ExecuteScalar<DateTime?>("SELECT MAX(StartedUtc) FROM Runs WHERE JobId=@jobId", new { jobId });
    }
}
