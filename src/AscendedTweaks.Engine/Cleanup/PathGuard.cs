namespace AscendedTweaks.Engine.Cleanup;

/// <summary>
/// Pfadpruefung nach Abschnitt 9.4: Kanonisierung, nur absolute lokale Laufwerkspfade, keine relativen Teile,
/// keine Alternate Data Streams, keine Geraete- oder UNC-Pfade, und nie in geschuetzten Ordnern.
/// </summary>
public static class PathGuard
{
    /// <summary>Kanonischer Pfad oder null, wenn der Pfad eine der Regeln verletzt.</summary>
    public static string? Canonical(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 32000)
        {
            return null;
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
        {
            return null; // UNC, \\?\ und \\.\
        }

        if (path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\')
        {
            return null;
        }

        if (path.IndexOf(':', 2) >= 0 || path.Any(char.IsControl) || path.IndexOfAny(['*', '?', '"', '<', '>', '|', '/']) >= 0)
        {
            return null; // Alternate Data Streams, Platzhalter
        }

        if (path.Split('\\').Any(s => s is "." or ".."))
        {
            return null;
        }

        try
        {
            var full = Path.GetFullPath(path).TrimEnd('\\');
            return string.Equals(full, path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>Echt unterhalb der Wurzel (nie die Wurzel selbst), beide bereits kanonisch.</summary>
    public static bool IsStrictlyUnder(string path, string root)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(root);
        return path.Length > root.Length + 1 && path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Ordner, die nie bereinigt werden (Abschnitt 6.5): Prefetch, WinSxS, Installer-Cache und Nutzerordner.</summary>
    public static IReadOnlyList<string> ProtectedFolders(string windows, string? userProfile)
    {
        var list = new List<string>
        {
            Path.Combine(windows, "Prefetch"),
            Path.Combine(windows, "WinSxS"),
            Path.Combine(windows, "Installer"),
            Path.Combine(windows, "System32"),
            Path.Combine(windows, "SysWOW64"),
        };
        if (userProfile is not null)
        {
            list.AddRange(new[] { "Documents", "Pictures", "Downloads", "Desktop", "Music", "Videos", "OneDrive" }.Select(f => Path.Combine(userProfile, f)));
        }

        return list;
    }

    public static bool IsProtected(string path, IReadOnlyList<string> protectedFolders)
    {
        return protectedFolders.Any(p => string.Equals(path, p, StringComparison.OrdinalIgnoreCase) || IsStrictlyUnder(path, p) || IsStrictlyUnder(p, path));
    }
}
