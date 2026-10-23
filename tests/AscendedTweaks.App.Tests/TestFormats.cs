using System.Globalization;

namespace AscendedTweaks.App.Tests;

/// <summary>
/// Number and date formats of one language for tests that check texts ("8,0 GB", "8.0 GB"). The app sets the formats from
/// its language at start; tests do not start the app, so without this they followed the machine (English on the GitHub
/// build machine, German on a German PC).
/// </summary>
internal sealed class TestFormats : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

    public TestFormats(string culture) => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

    public void Dispose() => CultureInfo.CurrentCulture = _previous;
}
