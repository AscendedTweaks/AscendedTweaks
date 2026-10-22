using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using AscendedTweaks.Core;
using AscendedTweaks.Engine.Insights;

namespace AscendedTweaks.Engine.Windows;

/// <summary>Startordner (Benutzer und alle Benutzer) und Anmelde-Aufgaben von Drittanbietern.</summary>
public sealed partial class WindowsAutostartSources : IAutostartSources
{
    private static readonly XNamespace TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private readonly ICommandRunner _runner;
    private readonly string _systemDirectory;

    public WindowsAutostartSources(ICommandRunner runner, string systemDirectory)
    {
        _runner = runner;
        _systemDirectory = systemDirectory;
    }

    /// <summary>
    /// Wertet die Ausgabe von "schtasks /Query /XML" aus: Jede Aufgabe steht hinter einem Kommentar mit ihrem Pfad
    /// und hat eine eigene XML-Deklaration. Aufgaben unter \Microsoft\ gehoeren zu Windows und werden ausgelassen.
    /// </summary>
    public static IReadOnlyList<LogonTask> ParseLogonTasks(string dump)
    {
        ArgumentNullException.ThrowIfNull(dump);
        var result = new List<LogonTask>();
        var matches = CommentPattern().Matches(dump);
        for (var i = 0; i < matches.Count; i++)
        {
            var path = matches[i].Groups["path"].Value.Trim();
            var start = matches[i].Index + matches[i].Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : dump.Length;
            if (path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var xml = DeclarationPattern().Replace(dump[start..end], string.Empty).Replace("</Tasks>", string.Empty, StringComparison.Ordinal);
            try
            {
                var root = XDocument.Parse(xml).Root;
                if (root?.Element(TaskNamespace + "Triggers")?.Element(TaskNamespace + "LogonTrigger") is null)
                {
                    continue;
                }

                var exec = root.Element(TaskNamespace + "Actions")?.Element(TaskNamespace + "Exec");
                var command = exec?.Element(TaskNamespace + "Command")?.Value.Trim();
                if (string.IsNullOrEmpty(command))
                {
                    continue;
                }

                var arguments = exec!.Element(TaskNamespace + "Arguments")?.Value.Trim();
                var enabledText = root.Element(TaskNamespace + "Settings")?.Element(TaskNamespace + "Enabled")?.Value.Trim();
                var quoted = command.StartsWith('"') ? command : $"\"{command}\"";
                result.Add(new LogonTask(path, string.IsNullOrEmpty(arguments) ? quoted : $"{quoted} {arguments}", !string.Equals(enabledText, "false", StringComparison.OrdinalIgnoreCase)));
            }
            catch (XmlException)
            {
                // Eine unlesbare Aufgabe wird ausgelassen; die Uebersicht ist reine Anzeige.
            }
        }

        return result;
    }

    public IReadOnlyList<LogonTask> LogonTasks()
    {
        var result = _runner.Run(Path.Combine(_systemDirectory, "schtasks.exe"), ["/Query", "/XML"], TimeSpan.FromSeconds(60));
        return result.ExitCode == 0 ? ParseLogonTasks(result.StandardOutput) : [];
    }

    [ExcludeFromCodeCoverage(Justification = "Reads only the file system and shortcuts.")]
    public IReadOnlyList<StartupFile> StartupFolderFiles()
    {
        var files = new List<StartupFile>();
        Add(files, Environment.GetFolderPath(Environment.SpecialFolder.Startup), machineWide: false);
        Add(files, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), machineWide: true);
        return files;
    }

    [ExcludeFromCodeCoverage(Justification = "Reads only the file system and shortcuts.")]
    private static void Add(List<StartupFile> files, string folder, bool machineWide)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(file);
            if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var (target, arguments) = Shortcut.Resolve(file);
                files.Add(new StartupFile(name, target, arguments, machineWide));
            }
            else
            {
                files.Add(new StartupFile(name, file, null, machineWide));
            }
        }
    }

    [GeneratedRegex(@"<!--\s*(?<path>\\[^>]*?)\s*-->", RegexOptions.CultureInvariant)]
    private static partial Regex CommentPattern();

    [GeneratedRegex(@"<\?xml[^>]*\?>", RegexOptions.CultureInvariant)]
    private static partial Regex DeclarationPattern();

    /// <summary>Ziel einer Verknuepfung ueber IShellLinkW (nur lesen, ohne Aufloesung ueber das Netzwerk).</summary>
    [ExcludeFromCodeCoverage(Justification = "COM-Huelle.")]
    private static class Shortcut
    {
        public static (string? Target, string? Arguments) Resolve(string path)
        {
            object? link = null;
            try
            {
                link = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))!);
                ((IPersistFile)link!).Load(path, 0);
                var shell = (IShellLinkW)link;
                var target = new StringBuilder(1024);
                shell.GetPath(target, target.Capacity, IntPtr.Zero, 0);
                var arguments = new StringBuilder(1024);
                shell.GetArguments(arguments, arguments.Capacity);
                return (target.Length > 0 ? target.ToString() : null, arguments.Length > 0 ? arguments.ToString() : null);
            }
            catch (COMException)
            {
                return (null, null);
            }
            finally
            {
                if (link is not null)
                {
                    Marshal.FinalReleaseComObject(link);
                }
            }
        }
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);

        void GetIDList(out IntPtr idList);

        void SetIDList(IntPtr idList);

        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int maxPath);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);

        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxPath);
    }
}

/// <summary>Authenticode-Pruefung ueber WinVerifyTrust (ohne Online-Sperrlistenabfrage), Herausgeber aus dem Zertifikat.</summary>
[ExcludeFromCodeCoverage(Justification = "Duenne Huelle um WinVerifyTrust.")]
public sealed class WindowsSignatureVerifier : ISignatureVerifier
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public SignatureInfo Verify(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        if (!File.Exists(filePath))
        {
            return new SignatureInfo(SignatureStatus.FileMissing, null);
        }

        var status = Check(filePath);
        string? publisher = null;
        if (status == SignatureStatus.Valid)
        {
            try
            {
#pragma warning disable SYSLIB0057 // CreateFromSignedFile liest nur das eingebettete Zertifikat; die Pruefung erfolgte oben.
                using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
#pragma warning restore SYSLIB0057
                publisher = certificate.GetNameInfo(X509NameType.SimpleName, false);
            }
            catch (CryptographicException)
            {
                publisher = null;
            }
        }

        return new SignatureInfo(status, publisher);
    }

    private static SignatureStatus Check(string filePath)
    {
        var fileInfo = new WinTrustFileInfo { Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(), FilePath = filePath };
        var filePointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, filePointer, false);
            var data = new WinTrustData
            {
                Size = (uint)Marshal.SizeOf<WinTrustData>(),
                UiChoice = 2, // WTD_UI_NONE
                RevocationChecks = 0, // WTD_REVOKE_NONE
                UnionChoice = 1, // WTD_CHOICE_FILE
                FileInfo = filePointer,
                StateAction = 0,
                ProviderFlags = 0x00001000, // WTD_CACHE_ONLY_URL_RETRIEVAL: keine Netzwerkzugriffe
            };
            var action = GenericVerifyV2;
            var result = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            return result switch
            {
                0 => SignatureStatus.Valid,
                unchecked((int)0x800B0100) => SignatureStatus.Unsigned, // TRUST_E_NOSIGNATURE
                _ => SignatureStatus.Invalid,
            };
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(filePointer);
            Marshal.FreeHGlobal(filePointer);
        }
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref WinTrustData data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }
}
