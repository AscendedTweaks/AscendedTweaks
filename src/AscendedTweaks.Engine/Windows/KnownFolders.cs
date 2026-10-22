using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace AscendedTweaks.Engine.Windows;

/// <summary>Bekannte Ordner, die .NET nicht als SpecialFolder kennt (SHGetKnownFolderPath); der Ordner kann umgeleitet sein.</summary>
[ExcludeFromCodeCoverage(Justification = "Duenne Win32-Huelle.")]
public static class KnownFolders
{
    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    /// <summary>FOLDERID_Downloads; null, wenn nicht ermittelbar.</summary>
    public static string? Downloads()
    {
        var id = DownloadsId;
        if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var pointer) != 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, IntPtr token, out IntPtr path);
}
