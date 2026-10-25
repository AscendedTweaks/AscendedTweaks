using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using AscendedTweaks.Engine.Insights;

namespace AscendedTweaks.Engine.Windows;

/// <summary>Starts what the user set up for "Ready to play" via the Windows shell; nothing is downloaded or elevated.</summary>
[ExcludeFromCodeCoverage(Justification = "Thin wrapper around Process.Start.")]
public sealed class WindowsProgramLauncher : IProgramLauncher
{
    public void Open(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();

    public void Start(string program, string arguments) =>
        Process.Start(new ProcessStartInfo(program, arguments) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden })?.Dispose();
}
