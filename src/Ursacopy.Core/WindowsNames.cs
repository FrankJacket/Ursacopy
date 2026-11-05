namespace Ursacopy.Core;

/// <summary>What NTFS and exFAT refuse in a file name (Linux filesystems such as Btrfs and ZFS accept all of it).</summary>
public static class WindowsNames
{
    static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Why <paramref name="name"/> cannot be stored on NTFS / exFAT, or null when it can.</summary>
    public static string? Problem(string name)
    {
        if (name.Length > 255) return "the name is longer than 255 characters";
        foreach (var c in name)
        {
            if (c < 32) return "the name contains a control character";
            if ("<>:\"/\\|?*".Contains(c)) return $"the name contains '{c}'";
        }
        if (name[^1] is '.' or ' ') return "the name ends with a dot or a space";
        var stem = name.Contains('.') ? name[..name.IndexOf('.')] : name;
        if (Reserved.Contains(stem.TrimEnd(' '))) return $"'{stem}' is a reserved name on Windows";
        return null;
    }
}
