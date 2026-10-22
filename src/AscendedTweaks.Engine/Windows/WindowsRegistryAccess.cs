using System.Security;
using AscendedTweaks.Core;
using Microsoft.Win32;

namespace AscendedTweaks.Engine.Windows;

/// <summary>Registry-Zugriff ueber Microsoft.Win32, immer in der 64-Bit-Ansicht.</summary>
public sealed class WindowsRegistryAccess : IRegistryAccess
{
    public RegistryData? GetValue(RegHive hive, string path, string name)
    {
        return Guard(path, () =>
        {
            using var key = Open(hive, path, writable: false);
            if (key is null || !key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                return null;
            }

            var kind = key.GetValueKind(name);
            var raw = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return kind switch
            {
                RegistryValueKind.DWord => RegistryData.DWord(unchecked((uint)(int)raw!)),
                RegistryValueKind.QWord => RegistryData.QWord(unchecked((ulong)(long)raw!)),
                RegistryValueKind.String => RegistryData.String((string)raw!),
                RegistryValueKind.ExpandString => RegistryData.ExpandString((string)raw!),
                RegistryValueKind.MultiString => RegistryData.MultiString((string[])raw!),
                RegistryValueKind.Binary => RegistryData.Binary((byte[])raw!),
                _ => throw new AscendedTweaksException(ErrorKind.Unavailable, $"Registry type {kind} is not supported."),
            };
        });
    }

    public bool KeyExists(RegHive hive, string path)
    {
        return Guard(path, () =>
        {
            using var key = Open(hive, path, writable: false);
            return key is not null;
        });
    }

    public IReadOnlyList<string> GetValueNames(RegHive hive, string path)
    {
        return Guard(path, () =>
        {
            using var key = Open(hive, path, writable: false);
            return (IReadOnlyList<string>?)key?.GetValueNames() ?? [];
        });
    }

    public IReadOnlyList<string> GetSubKeyNames(RegHive hive, string path)
    {
        return Guard(path, () =>
        {
            using var key = Open(hive, path, writable: false);
            return (IReadOnlyList<string>?)key?.GetSubKeyNames() ?? [];
        });
    }

    public void SetValue(RegHive hive, string path, string name, RegistryData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Guard(path, () =>
        {
            using var key = Base(hive).CreateSubKey(path, writable: true);
            switch (data.Kind)
            {
                case RegValueKind.DWord:
                    key.SetValue(name, unchecked((int)(uint)data.Number), RegistryValueKind.DWord);
                    break;
                case RegValueKind.QWord:
                    key.SetValue(name, unchecked((long)data.Number), RegistryValueKind.QWord);
                    break;
                case RegValueKind.String:
                    key.SetValue(name, data.Text!, RegistryValueKind.String);
                    break;
                case RegValueKind.ExpandString:
                    key.SetValue(name, data.Text!, RegistryValueKind.ExpandString);
                    break;
                case RegValueKind.MultiString:
                    key.SetValue(name, data.Lines!.ToArray(), RegistryValueKind.MultiString);
                    break;
                default:
                    key.SetValue(name, data.GetBytes()!, RegistryValueKind.Binary);
                    break;
            }

            return true;
        });
    }

    public void DeleteValue(RegHive hive, string path, string name)
    {
        Guard(path, () =>
        {
            using var key = Open(hive, path, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
            return true;
        });
    }

    public void CreateKey(RegHive hive, string path)
    {
        Guard(path, () =>
        {
            using var key = Base(hive).CreateSubKey(path, writable: false);
            return true;
        });
    }

    public void DeleteEmptyKey(RegHive hive, string path)
    {
        Guard(path, () =>
        {
            Base(hive).DeleteSubKey(path, throwOnMissingSubKey: false);
            return true;
        });
    }

    public void DeleteKeyTree(RegHive hive, string path)
    {
        Guard(path, () =>
        {
            Base(hive).DeleteSubKeyTree(path, throwOnMissingSubKey: false);
            return true;
        });
    }

    private static RegistryKey Base(RegHive hive)
    {
        var baseHive = hive switch
        {
            RegHive.HKCU => RegistryHive.CurrentUser,
            RegHive.HKU => RegistryHive.Users,
            _ => RegistryHive.LocalMachine,
        };
        return RegistryKey.OpenBaseKey(baseHive, RegistryView.Registry64);
    }

    private static RegistryKey? Open(RegHive hive, string path, bool writable)
    {
        using var root = Base(hive);
        return root.OpenSubKey(path, writable);
    }

    private static T Guard<T>(string path, Func<T> action)
    {
        if (!RegistryPaths.IsWellFormed(path, out var error))
        {
            throw new AscendedTweaksException(ErrorKind.CatalogInvalid, $"Invalid registry path {path}: {error}");
        }

        try
        {
            return action();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            throw new AscendedTweaksException(ErrorKind.PermissionDenied, $"Access to {path} denied.", innerException: ex);
        }
        catch (IOException ex)
        {
            throw new AscendedTweaksException(ErrorKind.General, $"Registry error at {path}: {ex.Message}", innerException: ex);
        }
    }
}
