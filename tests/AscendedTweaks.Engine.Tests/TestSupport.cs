using System.IO;
using AscendedTweaks.Core;
using AscendedTweaks.Engine.Executors;
using AscendedTweaks.Engine.State;

namespace AscendedTweaks.Engine.Tests;

internal sealed class Harness : IDisposable
{
    public FakeRegistry Registry { get; } = new();

    public FakeServices Services { get; } = new();

    public FakePower Power { get; } = new();

    public FakeCommands Commands { get; } = new();

    public FakeEnvironment Environment { get; } = new();

    public FakeTasks Tasks { get; } = new();

    public FakeParameters Parameters { get; } = new();

    public FakeRestorePoints RestorePoints { get; } = new();

    public string StateRoot { get; } = Path.Combine(Path.GetTempPath(), "AscendedTweaks.Tests", Guid.NewGuid().ToString("N"));

    public ExecutorSet Executors => new(Registry, Services, Power, Commands, Environment.SystemDirectory, Tasks, Parameters);

    public int TotalWrites => Registry.WriteCount + Services.WriteCount + Power.WriteCount + Commands.WriteCount + Tasks.WriteCount + Parameters.WriteCount;

    public StateStore Store => new(StateRoot, new FakeTime());

    public TweakEngine Engine(params TweakDefinition[] tweaks)
    {
        return new TweakEngine(new ListCatalog(tweaks), Executors, Store, Environment, RestorePoints, new FakeTime());
    }

    public void Dispose()
    {
        if (Directory.Exists(StateRoot))
        {
            Directory.Delete(StateRoot, recursive: true);
        }
    }
}

internal static class TestTweaks
{
    public const string Base = @"Software\AscendedTweaksTest";

    public static TweakDefinition Create(
        string id,
        DetectCondition detect,
        IReadOnlyList<TweakAction> apply,
        TweakScope scope = TweakScope.User,
        RiskLevel risk = RiskLevel.Safe,
        EvidenceLevel evidence = EvidenceLevel.Proven,
        IReadOnlyList<BuildRange>? builds = null,
        IReadOnlyList<WindowsEdition>? editions = null,
        RevertSpec? revert = null,
        bool requiresReboot = false,
        bool restartExplorer = false,
        bool hidden = false)
    {
        return new TweakDefinition
        {
            Id = id,
            Category = "Privacy.Test",
            NameKey = $"tweak.{id}.name",
            DescriptionKey = $"tweak.{id}.desc",
            SideEffectsKey = $"tweak.{id}.side",
            Risk = risk,
            Evidence = evidence,
            Builds = builds ?? [new BuildRange { Min = 19045 }],
            Editions = editions ?? [WindowsEdition.Home, WindowsEdition.Pro, WindowsEdition.Enterprise, WindowsEdition.Education],
            Scope = scope,
            RequiresAdmin = scope != TweakScope.User,
            RequiresReboot = requiresReboot,
            RestartExplorer = restartExplorer,
            HiddenByDefault = hidden,
            Detect = detect,
            Apply = apply,
            Revert = revert ?? RevertSpec.RestoreFromBackup,
            Sources = ["Testquelle fuer Unit-Tests"],
        };
    }

    public static TweakDefinition Dword(string id, string path, string name, uint value, RegHive hive = RegHive.HKCU, RiskLevel risk = RiskLevel.Safe)
    {
        return Create(
            id,
            new RegistryValueEqualsCondition { Hive = hive, Path = path, Name = name, Value = new RegLiteral { Number = value } },
            [new RegistrySetValueAction { Hive = hive, Path = path, Name = name, Kind = RegValueKind.DWord, Value = new RegLiteral { Number = value } }],
            RegistryPaths.ScopeFor(hive, path),
            risk);
    }

    public static TweakDefinition TwoDwords(string id, string path, string name1, string name2)
    {
        return Create(
            id,
            new AllOfCondition
            {
                Conditions =
                [
                    new RegistryValueEqualsCondition { Hive = RegHive.HKCU, Path = path, Name = name1, Value = new RegLiteral { Number = 0 } },
                    new RegistryValueEqualsCondition { Hive = RegHive.HKCU, Path = path, Name = name2, Value = new RegLiteral { Number = 0 } },
                ],
            },
            [
                new RegistrySetValueAction { Hive = RegHive.HKCU, Path = path, Name = name1, Kind = RegValueKind.DWord, Value = new RegLiteral { Number = 0 } },
                new RegistrySetValueAction { Hive = RegHive.HKCU, Path = path, Name = name2, Kind = RegValueKind.DWord, Value = new RegLiteral { Number = 0 } },
            ]);
    }

    public static TweakDefinition Service(string id, string service, ServiceStartSetting target)
    {
        return Create(
            id,
            new ServiceStartTypeEqualsCondition { Service = service, Value = target },
            [new ServiceSetStartTypeAction { Service = service, StartType = target, StopNow = true }],
            TweakScope.Machine);
    }

    public static TweakDefinition Power(string id, Guid scheme, Guid? duplicateFrom = null)
    {
        return Create(
            id,
            new PowerPlanActiveCondition { SchemeGuid = scheme, MissingIs = duplicateFrom is null ? TweakState.Unavailable : TweakState.NotApplied },
            [new PowerPlanSetAction { SchemeGuid = scheme, DuplicateFrom = duplicateFrom }],
            TweakScope.Machine);
    }

    public static TweakDefinition CreateKey(string id, string path)
    {
        return Create(
            id,
            new RegistryKeyExistsCondition { Hive = RegHive.HKCU, Path = path },
            [new RegistryCreateKeyAction { Hive = RegHive.HKCU, Path = path, DefaultValue = string.Empty }]);
    }
}
