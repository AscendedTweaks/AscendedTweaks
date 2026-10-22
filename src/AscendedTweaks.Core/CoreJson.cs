using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AscendedTweaks.Core;

/// <summary>Gemeinsame JSON-Einstellungen fuer Katalog, Sicherungen, Journal und Ledger.</summary>
public static class CoreJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new RegLiteralConverter());
        options.Converters.Add(new RevertSpecConverter());
        options.Converters.Add(new DiscriminatedConverter<TweakAction>(ActionTypeNames.Implemented, ActionTypeNames.Planned));
        options.Converters.Add(new DiscriminatedConverter<DetectCondition>(DetectTypeNames.All, new HashSet<string>()));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

/// <summary>Liest und schreibt abstrakte Typen ueber das Feld "type", unabhaengig von der Feldreihenfolge.</summary>
public sealed class DiscriminatedConverter<TBase> : JsonConverter<TBase>
    where TBase : class
{
    private readonly IReadOnlyDictionary<string, Type> _types;
    private readonly IReadOnlySet<string> _planned;

    public DiscriminatedConverter(IReadOnlyDictionary<string, Type> types, IReadOnlySet<string> planned)
    {
        _types = types;
        _planned = planned;
    }

    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(TBase);

    public override TBase? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("type", out var typeElement)
            || typeElement.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"{typeof(TBase).Name}: field 'type' is missing.");
        }

        var name = typeElement.GetString()!;
        if (!_types.TryGetValue(name, out var concrete))
        {
            throw new JsonException(_planned.Contains(name)
                ? $"Type '{name}' is not implemented in this version yet."
                : $"Unknown type '{name}'.");
        }

        var withoutType = new JsonObject();
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name != "type")
            {
                withoutType[property.Name] = JsonNode.Parse(property.Value.GetRawText());
            }
        }

        return (TBase?)withoutType.Deserialize(concrete, options);
    }

    public override void Write(Utf8JsonWriter writer, TBase value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        var name = _types.First(kv => kv.Value == value.GetType()).Key;
        var body = JsonSerializer.SerializeToNode(value, value.GetType(), options)!.AsObject();
        var result = new JsonObject { ["type"] = name };
        foreach (var property in body.ToList())
        {
            body.Remove(property.Key);
            result[property.Key] = property.Value;
        }

        result.WriteTo(writer, options);
    }
}

public sealed class RegLiteralConverter : JsonConverter<RegLiteral>
{
    public override RegLiteral Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (!reader.TryGetUInt64(out var number))
                {
                    throw new JsonException("Numbers must be whole numbers >= 0.");
                }

                return new RegLiteral { Number = number };
            case JsonTokenType.String:
                return new RegLiteral { Text = reader.GetString() };
            case JsonTokenType.StartArray:
                var lines = new List<string>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType != JsonTokenType.String)
                    {
                        throw new JsonException("MultiString lists may only contain strings.");
                    }

                    lines.Add(reader.GetString()!);
                }

                return new RegLiteral { Lines = lines };
            default:
                throw new JsonException("Value must be a number, a string or a list of strings.");
        }
    }

    public override void Write(Utf8JsonWriter writer, RegLiteral value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        if (value.Number is { } n)
        {
            writer.WriteNumberValue(n);
        }
        else if (value.Text is not null)
        {
            writer.WriteStringValue(value.Text);
        }
        else
        {
            writer.WriteStartArray();
            foreach (var line in value.Lines ?? [])
            {
                writer.WriteStringValue(line);
            }

            writer.WriteEndArray();
        }
    }
}

public sealed class RevertSpecConverter : JsonConverter<RevertSpec>
{
    private const string RestoreFromBackupName = "restoreFromBackup";

    public override RevertSpec Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString() == RestoreFromBackupName
                ? RevertSpec.RestoreFromBackup
                : throw new JsonException($"'revert' must be '{RestoreFromBackupName}' or a list of actions.");
        }

        var actions = JsonSerializer.Deserialize<List<TweakAction>>(ref reader, options)
            ?? throw new JsonException("'revert' is empty.");
        return new RevertSpec(actions);
    }

    public override void Write(Utf8JsonWriter writer, RevertSpec value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        if (value.IsRestoreFromBackup)
        {
            writer.WriteStringValue(RestoreFromBackupName);
        }
        else
        {
            JsonSerializer.Serialize(writer, value.Actions, options);
        }
    }
}
