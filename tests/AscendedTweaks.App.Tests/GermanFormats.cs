using System.Globalization;

namespace AscendedTweaks.App.Tests;

/// <summary>
/// German number and date formats for tests that check German texts ("8,0 GB"). The app sets the formats from its language
/// at start; tests do not start the app, so without this they followed the machine (English on the GitHub build machine).
/// </summary>
internal sealed class GermanFormats : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

    public GermanFormats() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

    public void Dispose() => CultureInfo.CurrentCulture = _previous;
}
