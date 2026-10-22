using System.Text;
using System.Text.Json;
using AscendedTweaks.Core;

namespace AscendedTweaks.Engine.Logging;

/// <summary>
/// JSON-Lines-Protokoll mit Rotation (Standard 10 Dateien zu je 5 MB).
/// Benutzername und Profilpfad werden durch %USERNAME% bzw. %USERPROFILE% ersetzt.
/// </summary>
public sealed class JsonLinesLog : ILogSink
{
    public const string FilePrefix = "ascendedtweaks";
    private static readonly JsonSerializerOptions Options = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly string _component;
    private readonly long _maxBytes;
    private readonly int _maxFiles;
    private readonly LogLevel _minimum;
    private readonly TimeProvider _time;
    private readonly (string Value, string Placeholder)[] _replacements;

    public JsonLinesLog(
        string directory,
        string component,
        TimeProvider time,
        LogLevel minimum = LogLevel.Information,
        long maxBytes = 5 * 1024 * 1024,
        int maxFiles = 10,
        string? userName = null,
        string? userProfile = null)
    {
        _directory = directory;
        _component = component;
        _time = time;
        _minimum = minimum;
        _maxBytes = maxBytes;
        _maxFiles = maxFiles;
        var replacements = new List<(string, string)>();
        userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        userName ??= Environment.UserName;
        if (!string.IsNullOrEmpty(userProfile))
        {
            replacements.Add((userProfile, "%USERPROFILE%"));
        }

        if (!string.IsNullOrEmpty(userName))
        {
            replacements.Add((userName, "%USERNAME%"));
        }

        _replacements = [.. replacements];
    }

    public static string DefaultUserDirectory =>
        Path.Combine(ProductInfo.DataDirectory(Environment.SpecialFolder.LocalApplicationData), "Logs");

    public static string DefaultMachineDirectory =>
        Path.Combine(ProductInfo.DataDirectory(Environment.SpecialFolder.CommonApplicationData), "Logs");

    public string CurrentFile => Path.Combine(_directory, $"{FilePrefix}-{_component}.jsonl");

    public void Write(LogRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Level < _minimum)
        {
            return;
        }

        var line = JsonSerializer.Serialize(
            new
            {
                ts = _time.GetUtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                level = record.Level.ToString(),
                component = _component,
                evt = record.Event,
                tweak = record.TweakId,
                target = Sanitize(record.Target),
                old = Sanitize(record.OldValue),
                @new = Sanitize(record.NewValue),
                result = record.Result,
                ms = record.DurationMs,
                error = Sanitize(record.Error),
            },
            Options);

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                RotateIfNeeded();
                File.AppendAllText(CurrentFile, line + "\n", Encoding.UTF8);
            }
            catch (IOException)
            {
                // Protokollieren darf nie den eigentlichen Vorgang abbrechen.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Loescht alle Protokolldateien dieser Komponente (Einstellungen: "Protokolle loeschen").</summary>
    public void Clear()
    {
        lock (_gate)
        {
            if (!Directory.Exists(_directory))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(_directory, $"{FilePrefix}-{_component}*.jsonl"))
            {
                File.Delete(file);
            }
        }
    }

    private string? Sanitize(string? text)
    {
        if (text is null)
        {
            return null;
        }

        foreach (var (value, placeholder) in _replacements)
        {
            text = text.Replace(value, placeholder, StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }

    private void RotateIfNeeded()
    {
        var current = new FileInfo(CurrentFile);
        if (!current.Exists || current.Length < _maxBytes)
        {
            return;
        }

        for (var i = _maxFiles - 1; i >= 1; i--)
        {
            var source = i == 1 ? CurrentFile : Rotated(i - 1);
            var target = Rotated(i);
            if (File.Exists(source))
            {
                File.Move(source, target, overwrite: true);
            }
        }
    }

    private string Rotated(int index) => Path.Combine(_directory, $"{FilePrefix}-{_component}.{index}.jsonl");
}
