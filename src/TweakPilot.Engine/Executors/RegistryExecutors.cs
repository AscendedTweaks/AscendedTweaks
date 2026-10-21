using TweakPilot.Core;

namespace TweakPilot.Engine.Executors;

internal static class RegistryOps
{
    public const int MaxTreeNodes = 5000;

    public static string ValueKey(RegHive hive, string path, string name) =>
        $"reg-value:{hive}\\{path}|{name}".ToUpperInvariant();

    public static string KeyKey(RegHive hive, string path) => $"reg-key:{hive}\\{path}".ToUpperInvariant();

    public static IReadOnlyList<string> MissingKeys(IRegistryAccess registry, RegHive hive, string path)
    {
        return RegistryPaths.Chain(path).Where(p => !registry.KeyExists(hive, p)).ToList();
    }

    public static bool IsEmpty(IRegistryAccess registry, RegHive hive, string path)
    {
        return registry.GetSubKeyNames(hive, path).Count == 0 && registry.GetValueNames(hive, path).Count == 0;
    }

    /// <summary>Stellt einen Wert her: loescht ihn, wenn er vorher fehlte, schreibt nur bei Abweichung.</summary>
    public static void RestoreValue(IRegistryAccess registry, RegHive hive, string path, string name, StoredRegistryData? before)
    {
        var current = registry.GetValue(hive, path, name);
        var original = StoredRegistryData.ToData(before);
        if (RegistryData.AreEqual(current, original))
        {
            return;
        }

        if (original is null)
        {
            registry.DeleteValue(hive, path, name);
        }
        else
        {
            registry.SetValue(hive, path, name, original);
        }
    }

    /// <summary>Entfernt von der App angelegte Schluessel, tiefste zuerst, aber nur wenn sie leer sind.</summary>
    public static void RemoveCreatedKeys(IRegistryAccess registry, RegHive hive, IReadOnlyList<string> createdKeys)
    {
        foreach (var key in createdKeys.Reverse())
        {
            if (registry.KeyExists(hive, key) && IsEmpty(registry, hive, key))
            {
                registry.DeleteEmptyKey(hive, key);
            }
        }
    }

    public static bool CreatedKeysGone(IRegistryAccess registry, RegHive hive, IReadOnlyList<string> createdKeys)
    {
        return createdKeys.All(k => !registry.KeyExists(hive, k) || !IsEmpty(registry, hive, k));
    }

    public static void EnsureKeyOperationAllowed(RegHive hive, string path)
    {
        if (!RegistryPaths.IsKeyOperationAllowed(hive, path))
        {
            throw new TweakPilotException(
                ErrorKind.CatalogInvalid,
                $"{RegistryPaths.Display(hive, path)} is not below an allowed root.");
        }
    }

    public static RegistryTreeNode? ReadTree(IRegistryAccess registry, RegHive hive, string path)
    {
        if (!registry.KeyExists(hive, path))
        {
            return null;
        }

        var count = 0;
        return ReadNode(registry, hive, path, path[(path.LastIndexOf('\\') + 1)..], ref count);
    }

    private static RegistryTreeNode ReadNode(IRegistryAccess registry, RegHive hive, string path, string name, ref int count)
    {
        if (++count > MaxTreeNodes)
        {
            throw new TweakPilotException(ErrorKind.BackupFailed, $"Subtree {RegistryPaths.Display(hive, path)} is too large for a backup.");
        }

        var values = registry.GetValueNames(hive, path)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(v => new RegistryTreeValue { Name = v, Data = StoredRegistryData.From(registry.GetValue(hive, path, v))! })
            .ToList();
        var subKeys = new List<RegistryTreeNode>();
        foreach (var sub in registry.GetSubKeyNames(hive, path).Order(StringComparer.OrdinalIgnoreCase))
        {
            subKeys.Add(ReadNode(registry, hive, path + "\\" + sub, sub, ref count));
        }

        return new RegistryTreeNode { Name = name, Values = values, SubKeys = subKeys };
    }

    public static void WriteTree(IRegistryAccess registry, RegHive hive, string path, RegistryTreeNode node)
    {
        registry.CreateKey(hive, path);
        foreach (var value in node.Values)
        {
            var data = value.Data.ToData();
            if (!RegistryData.AreEqual(registry.GetValue(hive, path, value.Name), data))
            {
                registry.SetValue(hive, path, value.Name, data);
            }
        }

        foreach (var sub in node.SubKeys)
        {
            WriteTree(registry, hive, path + "\\" + sub.Name, sub);
        }
    }

    public static bool TreesEqual(RegistryTreeNode? a, RegistryTreeNode? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        return string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
            && a.Values.Count == b.Values.Count
            && a.Values.Zip(b.Values).All(p =>
                string.Equals(p.First.Name, p.Second.Name, StringComparison.OrdinalIgnoreCase)
                && p.First.Data.Kind == p.Second.Data.Kind
                && string.Equals(p.First.Data.Base64, p.Second.Data.Base64, StringComparison.Ordinal))
            && a.SubKeys.Count == b.SubKeys.Count
            && a.SubKeys.Zip(b.SubKeys).All(p => TreesEqual(p.First, p.Second));
    }

    public static string DisplayTree(RegistryTreeNode? node)
    {
        return node is null ? EngineText.Missing : $"Key with {node.Values.Count} values and {node.SubKeys.Count} subkeys";
    }
}

internal sealed class RegistrySetValueExecutor : ActionExecutor<RegistrySetValueAction, RegistryValueCapture>
{
    private readonly IRegistryAccess _registry;

    public RegistrySetValueExecutor(IRegistryAccess registry) => _registry = registry;

    protected override string TargetKey(RegistrySetValueAction action) => RegistryOps.ValueKey(action.Hive, action.Path, action.Name);

    protected override RegistryValueCapture Capture(RegistrySetValueAction action)
    {
        return new RegistryValueCapture
        {
            Hive = action.Hive,
            Path = action.Path,
            Name = action.Name,
            Before = StoredRegistryData.From(_registry.GetValue(action.Hive, action.Path, action.Name)),
            MissingKeys = RegistryOps.MissingKeys(_registry, action.Hive, action.Path),
        };
    }

    protected override RegistryValueCapture Apply(RegistrySetValueAction action, RegistryValueCapture before)
    {
        var data = action.Value.ToData(action.Kind);
        _registry.SetValue(action.Hive, action.Path, action.Name, data);
        return before with { Applied = true, Written = StoredRegistryData.From(data) };
    }

    protected override bool Verify(RegistrySetValueAction action)
    {
        return RegistryData.AreEqual(_registry.GetValue(action.Hive, action.Path, action.Name), action.Value.ToData(action.Kind));
    }

    protected override void Restore(RegistryValueCapture capture)
    {
        RegistryOps.RestoreValue(_registry, capture.Hive, capture.Path, capture.Name, capture.Before);
        RegistryOps.RemoveCreatedKeys(_registry, capture.Hive, capture.MissingKeys);
    }

    protected override bool IsAtWrittenState(RegistryValueCapture capture)
    {
        return RegistryData.AreEqual(_registry.GetValue(capture.Hive, capture.Path, capture.Name), StoredRegistryData.ToData(capture.Written));
    }

    protected override bool IsAtOriginalState(RegistryValueCapture capture)
    {
        return RegistryData.AreEqual(_registry.GetValue(capture.Hive, capture.Path, capture.Name), StoredRegistryData.ToData(capture.Before))
            && RegistryOps.CreatedKeysGone(_registry, capture.Hive, capture.MissingKeys);
    }

    protected override PlanItem Describe(string tweakId, RegistrySetValueAction action)
    {
        return new PlanItem
        {
            TweakId = tweakId,
            ActionType = NameOf(action),
            Target = RegistryPaths.Display(action.Hive, action.Path),
            ValueName = action.Name,
            ValueKind = action.Kind.ToString(),
            Current = _registry.GetValue(action.Hive, action.Path, action.Name)?.Display() ?? EngineText.Missing,
            New = action.Value.ToData(action.Kind).Display(),
        };
    }

    protected override CaptureView View(RegistryValueCapture capture)
    {
        return new CaptureView(
            $"{RegistryPaths.Display(capture.Hive, capture.Path)} : {capture.Name}",
            _registry.GetValue(capture.Hive, capture.Path, capture.Name)?.Display() ?? EngineText.Missing,
            StoredRegistryData.DisplayOf(capture.Before),
            StoredRegistryData.DisplayOf(capture.Written));
    }
}

internal sealed class RegistryDeleteValueExecutor : ActionExecutor<RegistryDeleteValueAction, RegistryValueCapture>
{
    private readonly IRegistryAccess _registry;

    public RegistryDeleteValueExecutor(IRegistryAccess registry) => _registry = registry;

    protected override string TargetKey(RegistryDeleteValueAction action) => RegistryOps.ValueKey(action.Hive, action.Path, action.Name);

    protected override RegistryValueCapture Capture(RegistryDeleteValueAction action)
    {
        return new RegistryValueCapture
        {
            Hive = action.Hive,
            Path = action.Path,
            Name = action.Name,
            Before = StoredRegistryData.From(_registry.GetValue(action.Hive, action.Path, action.Name)),
        };
    }

    protected override RegistryValueCapture Apply(RegistryDeleteValueAction action, RegistryValueCapture before)
    {
        if (_registry.GetValue(action.Hive, action.Path, action.Name) is not null)
        {
            _registry.DeleteValue(action.Hive, action.Path, action.Name);
        }

        return before with { Applied = true, Written = null };
    }

    protected override bool Verify(RegistryDeleteValueAction action) => _registry.GetValue(action.Hive, action.Path, action.Name) is null;

    protected override void Restore(RegistryValueCapture capture)
    {
        RegistryOps.RestoreValue(_registry, capture.Hive, capture.Path, capture.Name, capture.Before);
    }

    protected override bool IsAtWrittenState(RegistryValueCapture capture) => _registry.GetValue(capture.Hive, capture.Path, capture.Name) is null;

    protected override bool IsAtOriginalState(RegistryValueCapture capture)
    {
        return RegistryData.AreEqual(_registry.GetValue(capture.Hive, capture.Path, capture.Name), StoredRegistryData.ToData(capture.Before));
    }

    protected override PlanItem Describe(string tweakId, RegistryDeleteValueAction action)
    {
        return new PlanItem
        {
            TweakId = tweakId,
            ActionType = NameOf(action),
            Target = RegistryPaths.Display(action.Hive, action.Path),
            ValueName = action.Name,
            Current = _registry.GetValue(action.Hive, action.Path, action.Name)?.Display() ?? EngineText.Missing,
            New = "(value is removed)",
        };
    }

    protected override CaptureView View(RegistryValueCapture capture)
    {
        return new CaptureView(
            $"{RegistryPaths.Display(capture.Hive, capture.Path)} : {capture.Name}",
            _registry.GetValue(capture.Hive, capture.Path, capture.Name)?.Display() ?? EngineText.Missing,
            StoredRegistryData.DisplayOf(capture.Before),
            EngineText.Missing);
    }
}

internal sealed class RegistryCreateKeyExecutor : ActionExecutor<RegistryCreateKeyAction, RegistryKeyCapture>
{
    private readonly IRegistryAccess _registry;

    public RegistryCreateKeyExecutor(IRegistryAccess registry) => _registry = registry;

    protected override string TargetKey(RegistryCreateKeyAction action) => RegistryOps.KeyKey(action.Hive, action.Path);

    protected override RegistryKeyCapture Capture(RegistryCreateKeyAction action)
    {
        return new RegistryKeyCapture
        {
            Hive = action.Hive,
            Path = action.Path,
            MissingKeys = RegistryOps.MissingKeys(_registry, action.Hive, action.Path),
            DefaultBefore = StoredRegistryData.From(_registry.GetValue(action.Hive, action.Path, string.Empty)),
        };
    }

    protected override RegistryKeyCapture Apply(RegistryCreateKeyAction action, RegistryKeyCapture before)
    {
        RegistryOps.EnsureKeyOperationAllowed(action.Hive, action.Path);
        _registry.CreateKey(action.Hive, action.Path);
        StoredRegistryData? written = null;
        if (action.DefaultValue is not null)
        {
            var data = RegistryData.String(action.DefaultValue);
            _registry.SetValue(action.Hive, action.Path, string.Empty, data);
            written = StoredRegistryData.From(data);
        }

        return before with { Applied = true, DefaultWritten = written };
    }

    protected override bool Verify(RegistryCreateKeyAction action)
    {
        return _registry.KeyExists(action.Hive, action.Path)
            && (action.DefaultValue is null
                || RegistryData.AreEqual(_registry.GetValue(action.Hive, action.Path, string.Empty), RegistryData.String(action.DefaultValue)));
    }

    protected override void Restore(RegistryKeyCapture capture)
    {
        if (_registry.KeyExists(capture.Hive, capture.Path))
        {
            RegistryOps.RestoreValue(_registry, capture.Hive, capture.Path, string.Empty, capture.DefaultBefore);
        }

        RegistryOps.RemoveCreatedKeys(_registry, capture.Hive, capture.MissingKeys);
    }

    protected override bool IsAtWrittenState(RegistryKeyCapture capture)
    {
        return _registry.KeyExists(capture.Hive, capture.Path)
            && (capture.DefaultWritten is null
                || RegistryData.AreEqual(_registry.GetValue(capture.Hive, capture.Path, string.Empty), capture.DefaultWritten.ToData()));
    }

    protected override bool IsAtOriginalState(RegistryKeyCapture capture)
    {
        if (!RegistryOps.CreatedKeysGone(_registry, capture.Hive, capture.MissingKeys))
        {
            return false;
        }

        return !_registry.KeyExists(capture.Hive, capture.Path)
            || RegistryData.AreEqual(_registry.GetValue(capture.Hive, capture.Path, string.Empty), StoredRegistryData.ToData(capture.DefaultBefore));
    }

    protected override PlanItem Describe(string tweakId, RegistryCreateKeyAction action)
    {
        var exists = _registry.KeyExists(action.Hive, action.Path);
        return new PlanItem
        {
            TweakId = tweakId,
            ActionType = NameOf(action),
            Target = RegistryPaths.Display(action.Hive, action.Path),
            ValueName = action.DefaultValue is null ? null : "(Standard)",
            Current = exists ? "key present" : "key missing",
            New = action.DefaultValue is null ? "key present" : $"key present, default value \"{action.DefaultValue}\"",
        };
    }

    protected override CaptureView View(RegistryKeyCapture capture)
    {
        return new CaptureView(
            RegistryPaths.Display(capture.Hive, capture.Path),
            _registry.KeyExists(capture.Hive, capture.Path) ? "key present" : "key missing",
            capture.MissingKeys.Count > 0 ? "key missing" : "key present",
            "key present");
    }
}

internal sealed class RegistryDeleteKeyExecutor : ActionExecutor<RegistryDeleteKeyAction, RegistryTreeCapture>
{
    private readonly IRegistryAccess _registry;

    public RegistryDeleteKeyExecutor(IRegistryAccess registry) => _registry = registry;

    protected override string TargetKey(RegistryDeleteKeyAction action) => RegistryOps.KeyKey(action.Hive, action.Path);

    protected override RegistryTreeCapture Capture(RegistryDeleteKeyAction action)
    {
        return new RegistryTreeCapture
        {
            Hive = action.Hive,
            Path = action.Path,
            Tree = RegistryOps.ReadTree(_registry, action.Hive, action.Path),
        };
    }

    protected override RegistryTreeCapture Apply(RegistryDeleteKeyAction action, RegistryTreeCapture before)
    {
        RegistryOps.EnsureKeyOperationAllowed(action.Hive, action.Path);
        if (_registry.KeyExists(action.Hive, action.Path))
        {
            _registry.DeleteKeyTree(action.Hive, action.Path);
        }

        return before with { Applied = true };
    }

    protected override bool Verify(RegistryDeleteKeyAction action) => !_registry.KeyExists(action.Hive, action.Path);

    protected override void Restore(RegistryTreeCapture capture)
    {
        if (capture.Tree is not null)
        {
            RegistryOps.WriteTree(_registry, capture.Hive, capture.Path, capture.Tree);
        }
    }

    protected override bool IsAtWrittenState(RegistryTreeCapture capture) => !_registry.KeyExists(capture.Hive, capture.Path);

    protected override bool IsAtOriginalState(RegistryTreeCapture capture)
    {
        return RegistryOps.TreesEqual(RegistryOps.ReadTree(_registry, capture.Hive, capture.Path), capture.Tree);
    }

    protected override PlanItem Describe(string tweakId, RegistryDeleteKeyAction action)
    {
        return new PlanItem
        {
            TweakId = tweakId,
            ActionType = NameOf(action),
            Target = RegistryPaths.Display(action.Hive, action.Path),
            Current = RegistryOps.DisplayTree(RegistryOps.ReadTree(_registry, action.Hive, action.Path)),
            New = "(key is removed with its contents)",
        };
    }

    protected override CaptureView View(RegistryTreeCapture capture)
    {
        return new CaptureView(
            RegistryPaths.Display(capture.Hive, capture.Path),
            RegistryOps.DisplayTree(RegistryOps.ReadTree(_registry, capture.Hive, capture.Path)),
            RegistryOps.DisplayTree(capture.Tree),
            EngineText.Missing);
    }
}
