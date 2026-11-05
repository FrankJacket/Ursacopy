using System.Runtime.InteropServices;

namespace Ursacopy.Core;

/// <summary>Flushes everything pending on a whole filesystem at once (Linux <c>syncfs</c>).</summary>
static class FsSync
{
    [DllImport("libc", SetLastError = true)]
    static extern int syncfs(int fd);

    /// <summary>True when the filesystem holding <paramref name="existingFile"/> was flushed; false when unsupported (caller falls back to per-file flushes).</summary>
    public static bool TrySyncFs(string existingFile)
    {
        if (!OperatingSystem.IsLinux()) return false;
        try
        {
            using var fs = new FileStream(existingFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return syncfs((int)fs.SafeFileHandle.DangerousGetHandle()) == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
