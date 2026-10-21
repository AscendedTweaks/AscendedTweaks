using System.Diagnostics.CodeAnalysis;
using System.Security.Principal;
using TweakPilot.Engine.Debloat;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace TweakPilot.Appx;

/// <summary>PackageManager-Zugriff. Lesen fuer den Benutzer ohne Administratorrechte; Entfernen fuer alle Benutzer nur im Helfer.</summary>
[ExcludeFromCodeCoverage(Justification = "Thin wrapper around PackageManager; the rules live in AppRemover and AppRestorer.")]
public sealed class WindowsAppxAccess : IAppxAccess
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);
    private readonly PackageManager _manager = new();

    public IReadOnlyList<AppxPackageInfo> ListForCurrentUser()
    {
        var sid = WindowsIdentity.GetCurrent().User!.Value;
        return _manager.FindPackagesForUser(sid).Select(Map).ToList();
    }

    public IReadOnlyList<AppxPackageInfo> FindForAllUsers(string name)
    {
        return _manager.FindPackages().Where(p => string.Equals(p.Id.Name, name, StringComparison.OrdinalIgnoreCase)).Select(Map).ToList();
    }

    public bool IsProvisioned(string familyName)
    {
        return _manager.FindProvisionedPackages().Any(p => string.Equals(p.Id.FamilyName, familyName, StringComparison.OrdinalIgnoreCase));
    }

    public void RemoveForAllUsers(string fullName) => Wait(_manager.RemovePackageAsync(fullName, RemovalOptions.RemoveForAllUsers));

    public void Deprovision(string familyName) => Wait(_manager.DeprovisionPackageForAllUsersAsync(familyName));

    public AppxRestoreOutcome RegisterByFamilyName(string familyName) =>
        Wait(_manager.RegisterPackageByFamilyNameAsync(familyName, null, DeploymentOptions.None, null, null));

    public AppxRestoreOutcome RegisterManifest(string manifestPath) =>
        Wait(_manager.RegisterPackageAsync(new Uri(manifestPath), null, DeploymentOptions.None));

    private static AppxRestoreOutcome Wait(Windows.Foundation.IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> operation)
    {
        var task = operation.AsTask();
        if (!task.Wait(Timeout))
        {
            throw new InvalidOperationException("Package manager time limit exceeded.");
        }

        var result = task.Result;
        return result.IsRegistered || result.ExtendedErrorCode is null ? AppxRestoreOutcome.Registered : throw new InvalidOperationException(result.ErrorText);
    }

    private static AppxPackageInfo Map(Package package)
    {
        string? location;
        try
        {
            location = package.InstalledLocation?.Path;
        }
        catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            location = null;
        }

        var publisher = package.Id.Publisher ?? string.Empty;
        return new AppxPackageInfo(
            package.Id.Name,
            package.Id.FullName,
            package.Id.FamilyName,
            Safe(() => package.PublisherDisplayName) ?? string.Empty,
            publisher.Contains("O=Microsoft Corporation", StringComparison.OrdinalIgnoreCase),
            package.IsFramework,
            package.IsResourcePackage,
            Safe(() => package.SignatureKind) == PackageSignatureKind.System,
            location);
    }

    private static T? Safe<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return default;
        }
    }
}
