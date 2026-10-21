using System.Globalization;
using System.Text;

namespace TweakPilot.Core;

public enum RegHive
{
    HKCU,
    HKLM,

    /// <summary>Nur intern im Helfer (HKEY_USERS\SID); im Katalog nicht erlaubt.</summary>
    HKU,
}

public enum RegValueKind
{
    String,
    ExpandString,
    DWord,
    QWord,
    MultiString,
    Binary,
}

/// <summary>
/// Unveraenderlicher, typisierter Registry-Wert ohne Abhaengigkeit von Windows-APIs.
/// Rohdarstellung entspricht dem Registry-Format (DWord/QWord little endian, Zeichenketten UTF-16LE mit Nullterminator).
/// </summary>
public sealed class RegistryData : IEquatable<RegistryData>
{
    private readonly string[]? _lines;
    private readonly byte[]? _bytes;

    private RegistryData(RegValueKind kind, ulong number, string? text, string[]? lines, byte[]? bytes)
    {
        Kind = kind;
        Number = number;
        Text = text;
        _lines = lines;
        _bytes = bytes;
    }

    public RegValueKind Kind { get; }

    /// <summary>Wert bei DWord und QWord.</summary>
    public ulong Number { get; }

    /// <summary>Wert bei String und ExpandString.</summary>
    public string? Text { get; }

    /// <summary>Wert bei MultiString.</summary>
    public IReadOnlyList<string>? Lines => _lines;

    /// <summary>Wert bei Binary (Kopie).</summary>
    public byte[]? GetBytes() => _bytes is null ? null : (byte[])_bytes.Clone();

    public static RegistryData DWord(uint value) => new(RegValueKind.DWord, value, null, null, null);

    public static RegistryData QWord(ulong value) => new(RegValueKind.QWord, value, null, null, null);

    public static RegistryData String(string value) => new(RegValueKind.String, 0, value, null, null);

    public static RegistryData ExpandString(string value) => new(RegValueKind.ExpandString, 0, value, null, null);

    public static RegistryData MultiString(IEnumerable<string> lines) => new(RegValueKind.MultiString, 0, null, lines.ToArray(), null);

    public static RegistryData Binary(byte[] bytes) => new(RegValueKind.Binary, 0, null, null, (byte[])bytes.Clone());

    public byte[] ToRaw()
    {
        switch (Kind)
        {
            case RegValueKind.DWord:
                return BitConverter.GetBytes((uint)Number);
            case RegValueKind.QWord:
                return BitConverter.GetBytes(Number);
            case RegValueKind.String:
            case RegValueKind.ExpandString:
                return Encoding.Unicode.GetBytes(Text + "\0");
            case RegValueKind.MultiString:
                var joined = _lines!.Length == 0 ? "\0" : string.Join('\0', _lines) + "\0\0";
                return Encoding.Unicode.GetBytes(joined);
            default:
                return (byte[])_bytes!.Clone();
        }
    }

    public static RegistryData FromRaw(RegValueKind kind, byte[] raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        switch (kind)
        {
            case RegValueKind.DWord:
                if (raw.Length != 4)
                {
                    throw new FormatException("DWord needs exactly 4 bytes.");
                }

                return DWord(BitConverter.ToUInt32(raw, 0));
            case RegValueKind.QWord:
                if (raw.Length != 8)
                {
                    throw new FormatException("QWord needs exactly 8 bytes.");
                }

                return QWord(BitConverter.ToUInt64(raw, 0));
            case RegValueKind.String:
                return String(Encoding.Unicode.GetString(raw).TrimEnd('\0'));
            case RegValueKind.ExpandString:
                return ExpandString(Encoding.Unicode.GetString(raw).TrimEnd('\0'));
            case RegValueKind.MultiString:
                var content = Encoding.Unicode.GetString(raw).TrimEnd('\0');
                return MultiString(content.Length == 0 ? [] : content.Split('\0'));
            default:
                return Binary(raw);
        }
    }

    public string Display()
    {
        return Kind switch
        {
            RegValueKind.DWord or RegValueKind.QWord =>
                string.Create(CultureInfo.InvariantCulture, $"{Kind} {Number} (0x{Number:X})"),
            RegValueKind.String or RegValueKind.ExpandString => $"{Kind} \"{Text}\"",
            RegValueKind.MultiString => $"{Kind} [{string.Join(", ", _lines!.Select(l => $"\"{l}\""))}]",
            _ => $"{Kind} {Convert.ToHexString(_bytes!)}",
        };
    }

    public override string ToString() => Display();

    public bool Equals(RegistryData? other)
    {
        if (other is null || other.Kind != Kind)
        {
            return false;
        }

        return Kind switch
        {
            RegValueKind.DWord or RegValueKind.QWord => Number == other.Number,
            RegValueKind.String or RegValueKind.ExpandString => string.Equals(Text, other.Text, StringComparison.Ordinal),
            RegValueKind.MultiString => _lines!.SequenceEqual(other._lines!, StringComparer.Ordinal),
            _ => _bytes!.AsSpan().SequenceEqual(other._bytes),
        };
    }

    public override bool Equals(object? obj) => Equals(obj as RegistryData);

    public override int GetHashCode() => HashCode.Combine(Kind, Number, Text);

    public static bool AreEqual(RegistryData? a, RegistryData? b) => a is null ? b is null : a.Equals(b);
}

/// <summary>
/// Wert-Literal aus dem Katalog: Zahl, Zeichenkette oder Liste von Zeichenketten.
/// Bei Binary wird die Zeichenkette als Base64 gelesen.
/// </summary>
public sealed record RegLiteral
{
    public ulong? Number { get; init; }

    public string? Text { get; init; }

    public IReadOnlyList<string>? Lines { get; init; }

    public bool TryToData(RegValueKind kind, out RegistryData? data, out string? error)
    {
        data = null;
        error = null;
        switch (kind)
        {
            case RegValueKind.DWord when Number is { } n && n <= uint.MaxValue:
                data = RegistryData.DWord((uint)n);
                return true;
            case RegValueKind.QWord when Number is { } q:
                data = RegistryData.QWord(q);
                return true;
            case RegValueKind.String when Text is not null:
                data = RegistryData.String(Text);
                return true;
            case RegValueKind.ExpandString when Text is not null:
                data = RegistryData.ExpandString(Text);
                return true;
            case RegValueKind.MultiString when Lines is not null:
                data = RegistryData.MultiString(Lines);
                return true;
            case RegValueKind.Binary when Text is not null:
                try
                {
                    data = RegistryData.Binary(Convert.FromBase64String(Text));
                    return true;
                }
                catch (FormatException)
                {
                    error = "Binary value is not valid Base64.";
                    return false;
                }

            default:
                error = $"Literal {this} does not match type {kind}.";
                return false;
        }
    }

    public RegistryData ToData(RegValueKind kind)
    {
        return TryToData(kind, out var data, out var error)
            ? data!
            : throw new TweakPilotException(ErrorKind.CatalogInvalid, error!);
    }

    public bool Matches(RegistryData? actual)
    {
        if (actual is null)
        {
            return false;
        }

        return actual.Kind switch
        {
            RegValueKind.DWord or RegValueKind.QWord => Number == actual.Number,
            RegValueKind.String or RegValueKind.ExpandString => Text is not null && string.Equals(Text, actual.Text, StringComparison.Ordinal),
            RegValueKind.MultiString => Lines is not null && Lines.SequenceEqual(actual.Lines!, StringComparer.Ordinal),
            _ => Text is not null && string.Equals(Text, Convert.ToBase64String(actual.GetBytes()!), StringComparison.Ordinal),
        };
    }

    public bool SameAs(RegLiteral other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Number == other.Number
            && string.Equals(Text, other.Text, StringComparison.Ordinal)
            && (Lines is null ? other.Lines is null : other.Lines is not null && Lines.SequenceEqual(other.Lines, StringComparer.Ordinal));
    }

    public override string ToString()
    {
        if (Number is { } n)
        {
            return n.ToString(CultureInfo.InvariantCulture);
        }

        if (Text is not null)
        {
            return $"\"{Text}\"";
        }

        return Lines is null ? "(leer)" : $"[{string.Join(", ", Lines)}]";
    }
}
