namespace Ursacopy.Core;

/// <summary>
/// The marker file at the root of the external disk. A run only writes when the marker found there carries the id the job
/// expects: if the USB disk is not mounted, the container sees an empty folder of the NAS system disk and must not fill it.
/// </summary>
public static class TargetMarker
{
    public const string FileName = ".ursacopy-target";

    /// <summary>The id stored on the disk, or null when the folder has no (readable) marker.</summary>
    public static string? Read(string targetDir)
    {
        try
        {
            var path = Path.Combine(targetDir, FileName);
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Writes a new marker, or returns the existing one so that initialising twice never orphans a job.</summary>
    public static string Initialize(string targetDir)
    {
        if (!Directory.Exists(targetDir)) throw new DirectoryNotFoundException($"The target folder '{targetDir}' does not exist.");
        var existing = Read(targetDir);
        if (!string.IsNullOrEmpty(existing)) return existing;
        var id = Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(targetDir, FileName), id + Environment.NewLine);
        return id;
    }
}
