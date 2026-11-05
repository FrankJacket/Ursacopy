using System.Text.RegularExpressions;

namespace Ursacopy.Core;

/// <summary>Gitignore-like but simpler: <c>*</c> and <c>?</c> only, case-insensitive. A pattern without a slash matches a name at any depth.</summary>
public sealed class ExcludeMatcher
{
    /// <summary>Folders and files created by NAS firmwares and operating systems that nobody wants on a copy.</summary>
    public static readonly string[] NasDefaults =
        ["@eaDir", "#recycle", "@Recycle", ".DS_Store", "Thumbs.db", "lost+found", "$RECYCLE.BIN", "System Volume Information"];

    readonly List<(Regex Rx, bool OnPath)> _rules = [];

    public ExcludeMatcher(IEnumerable<string> patterns)
    {
        foreach (var raw in patterns)
        {
            var p = raw.Trim().Replace('\\', '/').Trim('/');
            if (p.Length == 0) continue; // no comment syntax: "#recycle" is a real folder name
            var rx = "^" + Regex.Escape(p).Replace("\\*", "[^/]*").Replace("\\?", "[^/]") + "$";
            _rules.Add((new Regex(rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), p.Contains('/')));
        }
    }

    public bool IsExcluded(string relativePath, string name)
    {
        foreach (var (rx, onPath) in _rules)
            if (rx.IsMatch(onPath ? relativePath : name)) return true;
        return false;
    }
}
