namespace AscendedTweaks.Core;

/// <summary>
/// Central product constants. A rename happens here and in Directory.Build.props (build metadata).
/// </summary>
public static class ProductInfo
{
    /// <summary>Name shown in windows, shortcuts and messages.</summary>
    public const string Name = "Ascended Tweaks";

    /// <summary>Name without spaces for folders, files, pipes and registry keys.</summary>
    public const string Id = "AscendedTweaks";

    /// <summary>Earlier product name. Its data folders are taken over so that backups and applied tweaks stay available.</summary>
    public const string LegacyId = "TweakPilot";

    public const string CliExecutable = "ascendedtweaks-cli";

    /// <summary>Data folder of the product under a Windows base folder such as %LOCALAPPDATA% or %ProgramData%.</summary>
    public static string DataDirectory(Environment.SpecialFolder baseFolder) => DataDirectory(Environment.GetFolderPath(baseFolder));

    /// <summary>
    /// Returns <c>baseDirectory\AscendedTweaks</c>. If only the folder of the earlier name exists, it is moved there once. If moving
    /// fails (for example %ProgramData% without administrator rights, or a file is in use), the earlier folder is used as it is.
    /// </summary>
    public static string DataDirectory(string baseDirectory)
    {
        var current = Path.Combine(baseDirectory, Id);
        var legacy = Path.Combine(baseDirectory, LegacyId);
        if (Directory.Exists(current) || !Directory.Exists(legacy))
        {
            return current;
        }

        try
        {
            Directory.Move(legacy, current);
            return current;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return legacy;
        }
    }
}
