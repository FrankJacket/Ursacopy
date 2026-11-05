using Ursacopy.Core;
using Ursacopy.Server.Data;

namespace Ursacopy.Server;

public enum DiskState { Missing, NotInitialised, Connected, OtherDisk }

public sealed record DiskInfo(DiskState State, long FreeBytes, long TotalBytes);

public static class DiskStatus
{
    public static DiskInfo Check(Job j)
    {
        try
        {
            if (!Directory.Exists(j.TargetDir)) return new(DiskState.Missing, 0, 0);
            var onDisk = TargetMarker.Read(j.TargetDir);
            if (j.TargetId is null) return new(DiskState.NotInitialised, 0, 0);
            if (onDisk is null) return new(DiskState.Missing, 0, 0);
            if (onDisk != j.TargetId) return new(DiskState.OtherDisk, 0, 0);
            long free = 0, total = 0;
            try { var d = new DriveInfo(j.TargetDir); free = d.AvailableFreeSpace; total = d.TotalSize; } catch { /* size unknown */ }
            return new(DiskState.Connected, free, total);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new(DiskState.Missing, 0, 0); }
    }
}

/// <summary>Tells whether a folder is a mount point, from /proc/self/mountinfo (Linux containers only).</summary>
public static class MountInfo
{
    /// <summary>True / false, or null when it cannot be determined (not Linux, no /proc).</summary>
    public static bool? IsMountPoint(string path)
    {
        const string file = "/proc/self/mountinfo";
        if (!OperatingSystem.IsLinux() || !File.Exists(file)) return null;
        try
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            foreach (var line in File.ReadLines(file))
            {
                var f = line.Split(' ');
                if (f.Length > 4 && Unescape(f[4]) == full) return true;
            }
            return false;
        }
        catch (IOException) { return null; }
    }

    // mountinfo writes space, tab, newline and backslash as \040 \011 \012 \134
    static string Unescape(string s) => s.Contains('\\')
        ? System.Text.RegularExpressions.Regex.Replace(s, @"\\([0-7]{3})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 8)).ToString())
        : s;
}

public static class Fmt
{
    public static string Bytes(long b)
    {
        string[] u = ["B", "KB", "MB", "GB", "TB"];
        double v = b; int i = 0;
        while (v >= 1000 && i < u.Length - 1) { v /= 1000; i++; }
        return i == 0 ? $"{b} B" : $"{v:0.#} {u[i]}";
    }

    public static string Duration(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes:00} min" : t.TotalMinutes >= 1 ? $"{t.Minutes} min {t.Seconds:00} s" : $"{t.Seconds} s";

    public static string Local(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
