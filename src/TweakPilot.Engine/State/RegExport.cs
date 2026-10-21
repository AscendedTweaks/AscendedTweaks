using System.Text;
using TweakPilot.Core;

namespace TweakPilot.Engine.State;

/// <summary>
/// Menschenlesbarer .reg-Export der gesicherten Originalwerte (Abschnitt 7.2) fuer Transparenz und manuelle Wiederherstellung.
/// </summary>
internal static class RegExport
{
    public static string? Build(IEnumerable<ActionCapture> captures)
    {
        var builder = new StringBuilder();
        var any = false;
        builder.AppendLine("Windows Registry Editor Version 5.00");
        builder.AppendLine();
        builder.AppendLine($"; {ProductInfo.Name}: original values before applying");

        foreach (var capture in captures)
        {
            switch (capture)
            {
                case RegistryValueCapture c:
                    any = true;
                    builder.AppendLine();
                    builder.AppendLine($"[{HiveName(c.Hive)}\\{c.Path}]");
                    builder.AppendLine(Line(c.Name, c.Before));
                    break;
                case RegistryListEntryCapture c:
                    any = true;
                    builder.AppendLine();
                    builder.AppendLine($"; Entry {c.Key}: before {c.BeforeEntry ?? "(missing)"}; when resetting, the app resets only this entry.");
                    builder.AppendLine($"[{HiveName(c.Hive)}\\{c.Path}]");
                    builder.AppendLine(Line(c.Name, c.Before));
                    break;
                case RegistryKeyCapture c:
                    any = true;
                    builder.AppendLine();
                    if (c.MissingKeys.Count > 0)
                    {
                        builder.AppendLine($"; Key did not exist before, restoring = deleting:");
                        builder.AppendLine($"[-{HiveName(c.Hive)}\\{c.MissingKeys[0]}]");
                    }
                    else
                    {
                        builder.AppendLine($"[{HiveName(c.Hive)}\\{c.Path}]");
                        builder.AppendLine(Line(string.Empty, c.DefaultBefore));
                    }

                    break;
                case RegistryTreeCapture c when c.Tree is not null:
                    any = true;
                    AppendTree(builder, $"{HiveName(c.Hive)}\\{c.Path}", c.Tree);
                    break;
            }
        }

        return any ? builder.ToString() : null;
    }

    private static void AppendTree(StringBuilder builder, string path, RegistryTreeNode node)
    {
        builder.AppendLine();
        builder.AppendLine($"[{path}]");
        foreach (var value in node.Values)
        {
            builder.AppendLine(Line(value.Name, value.Data));
        }

        foreach (var sub in node.SubKeys)
        {
            AppendTree(builder, $"{path}\\{sub.Name}", sub);
        }
    }

    private static string HiveName(RegHive hive) => hive switch
    {
        RegHive.HKCU => "HKEY_CURRENT_USER",
        RegHive.HKU => "HKEY_USERS",
        _ => "HKEY_LOCAL_MACHINE",
    };

    private static string Line(string name, StoredRegistryData? data)
    {
        var left = name.Length == 0 ? "@" : $"\"{Escape(name)}\"";
        if (data is null)
        {
            return $"{left}=-";
        }

        var value = data.ToData();
        return value.Kind switch
        {
            RegValueKind.String => $"{left}=\"{Escape(value.Text!)}\"",
            RegValueKind.DWord => $"{left}=dword:{value.Number:x8}",
            RegValueKind.QWord => $"{left}=hex(b):{Hex(value.ToRaw())}",
            RegValueKind.ExpandString => $"{left}=hex(2):{Hex(value.ToRaw())}",
            RegValueKind.MultiString => $"{left}=hex(7):{Hex(value.ToRaw())}",
            _ => $"{left}=hex:{Hex(value.ToRaw())}",
        };
    }

    private static string Escape(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string Hex(byte[] bytes) => string.Join(',', bytes.Select(b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
}
