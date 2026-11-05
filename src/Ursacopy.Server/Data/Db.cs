using Dapper;
using Microsoft.Data.Sqlite;

namespace Ursacopy.Server.Data;

public sealed class Db(IConfiguration cfg)
{
    public string DataDir { get; } = Path.GetFullPath(cfg["DataDir"] ?? "data");

    public SqliteConnection Open()
    {
        var c = new SqliteConnection($"Data Source={Path.Combine(DataDir, "ursacopy.db")};Pooling=True");
        c.Open();
        return c;
    }

    public void Init()
    {
        Directory.CreateDirectory(DataDir);
        using var c = Open();
        c.Execute("PRAGMA journal_mode=WAL;");
        c.Execute("""
            CREATE TABLE IF NOT EXISTS Jobs (
              Id INTEGER PRIMARY KEY AUTOINCREMENT,
              Name TEXT NOT NULL, SourceDir TEXT NOT NULL, TargetDir TEXT NOT NULL, DestFolder TEXT NOT NULL,
              TargetId TEXT NULL, Enabled INTEGER NOT NULL, ScheduleMode INTEGER NOT NULL, DailyTime TEXT NOT NULL,
              MirrorDeletes INTEGER NOT NULL, RetentionDays INTEGER NOT NULL, Verify INTEGER NOT NULL,
              Excludes TEXT NOT NULL, CreatedUtc TEXT NOT NULL, EstimatedFiles INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS Runs (
              Id INTEGER PRIMARY KEY AUTOINCREMENT,
              JobId INTEGER NOT NULL, JobName TEXT NOT NULL, StartedUtc TEXT NOT NULL, EndedUtc TEXT NULL,
              Status TEXT NOT NULL, Reason TEXT NOT NULL, FilesSeen INTEGER NOT NULL DEFAULT 0, Copied INTEGER NOT NULL DEFAULT 0,
              BytesCopied INTEGER NOT NULL DEFAULT 0, Unchanged INTEGER NOT NULL DEFAULT 0, Deleted INTEGER NOT NULL DEFAULT 0,
              Errors INTEGER NOT NULL DEFAULT 0, Message TEXT NULL, Details TEXT NULL);
            CREATE INDEX IF NOT EXISTS IX_Runs_Job ON Runs(JobId, StartedUtc);
            """);
        // Column added after the first release.
        if (!c.Query<string>("SELECT name FROM pragma_table_info('Jobs')").Contains("Includes"))
            c.Execute("ALTER TABLE Jobs ADD COLUMN Includes TEXT NOT NULL DEFAULT ''");
    }
}
