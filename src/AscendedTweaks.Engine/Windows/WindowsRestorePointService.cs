using System.Diagnostics.CodeAnalysis;
using System.Management;
using AscendedTweaks.Core;

namespace AscendedTweaks.Engine.Windows;

/// <summary>
/// Wiederherstellungspunkt ueber die WMI-Klasse SystemRestore (root\default), best effort (Regel S1a).
/// Windows legt standardmaessig hoechstens einen Punkt je 24 Stunden an und meldet dann trotzdem Erfolg;
/// deshalb wird vorher und nachher die hoechste Sequenznummer verglichen.
/// Die App aendert SystemRestorePointCreationFrequency nicht.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WMI wrapper, testable only with administrator rights in the VM.")]
public sealed class WindowsRestorePointService : IRestorePointService
{
    private const int ModifySettings = 12;
    private const int BeginSystemChange = 100;
    private const uint ErrorServiceDisabled = 1058;

    public RestorePointResult TryCreate(string description)
    {
        try
        {
            var before = HighestSequence();
            using var restore = new ManagementClass(@"\\.\root\default", "SystemRestore", null);
            using var parameters = restore.GetMethodParameters("CreateRestorePoint");
            parameters["Description"] = description.Length > 64 ? description[..64] : description;
            parameters["RestorePointType"] = ModifySettings;
            parameters["EventType"] = BeginSystemChange;
            using var output = restore.InvokeMethod("CreateRestorePoint", parameters, null);
            var code = Convert.ToUInt32(output["ReturnValue"], System.Globalization.CultureInfo.InvariantCulture);
            if (code == ErrorServiceDisabled)
            {
                return new RestorePointResult(RestorePointStatus.ProtectionDisabled, "System protection is turned off on the system drive.");
            }

            if (code != 0)
            {
                return new RestorePointResult(RestorePointStatus.Failed, $"Windows refused the restore point (code {code}).");
            }

            return HighestSequence() > before
                ? new RestorePointResult(RestorePointStatus.Created, "Restore point created.")
                : new RestorePointResult(RestorePointStatus.AlreadyExistsWithin24Hours, "A restore point already exists from the last 24 hours. Ascended Tweaks still creates its own backup.");
        }
        catch (ManagementException ex)
        {
            return new RestorePointResult(RestorePointStatus.Failed, $"Restore point not possible: {ex.Message}");
        }
        catch (UnauthorizedAccessException)
        {
            return new RestorePointResult(RestorePointStatus.Failed, "Restore points need administrator rights.");
        }
    }

    /// <summary>Schaltet den Computerschutz fuer ein Laufwerk ein. Nur nach ausdruecklicher Zustimmung aufrufen.</summary>
    public static bool EnableProtection(string drive)
    {
        using var restore = new ManagementClass(@"\\.\root\default", "SystemRestore", null);
        using var parameters = restore.GetMethodParameters("Enable");
        parameters["Drive"] = drive;
        using var output = restore.InvokeMethod("Enable", parameters, null);
        return Convert.ToUInt32(output["ReturnValue"], System.Globalization.CultureInfo.InvariantCulture) == 0;
    }

    private static uint HighestSequence()
    {
        using var searcher = new ManagementObjectSearcher(@"\\.\root\default", "SELECT SequenceNumber FROM SystemRestore");
        uint highest = 0;
        foreach (var item in searcher.Get())
        {
            using (item)
            {
                highest = Math.Max(highest, Convert.ToUInt32(item["SequenceNumber"], System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return highest;
    }
}
