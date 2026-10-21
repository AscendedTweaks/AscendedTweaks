namespace TweakPilot.Core;

public enum BuildSupport
{
    Supported,

    /// <summary>Neuer als der hoechste getestete Build: Hinweis anzeigen, Experten-Tweaks sperren.</summary>
    Untested,

    /// <summary>Zu alt: nur Lesemodus.</summary>
    Unsupported,
}

/// <summary>Windows-Versionscheck nach Regel S11.</summary>
public static class SupportPolicy
{
    public const int MinWindows10Build = 19045;

    public const int FirstWindows11Build = 22000;

    public const int MinWindows11Build = 22631;

    /// <summary>ANNAHME: hoechster getesteter Build (siehe docs/DECISIONS.md).</summary>
    public const int HighestTestedBuild = 26200;

    public static BuildSupport Classify(int build)
    {
        if (build < MinWindows10Build)
        {
            return BuildSupport.Unsupported;
        }

        if (build < FirstWindows11Build)
        {
            return BuildSupport.Supported;
        }

        if (build < MinWindows11Build)
        {
            return BuildSupport.Unsupported;
        }

        return build <= HighestTestedBuild ? BuildSupport.Supported : BuildSupport.Untested;
    }
}
