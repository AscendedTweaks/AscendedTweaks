using System.Text.Json;
using AscendedTweaks.Core;
using FluentAssertions;
using Xunit;

namespace AscendedTweaks.Engine.Tests;

public sealed class CoreTests
{
    public static TheoryData<RegistryData> AllKinds() => new()
    {
        RegistryData.DWord(0xFFFFFFFF),
        RegistryData.QWord(864000000000),
        RegistryData.String("Text mit Umlaut ä"),
        RegistryData.ExpandString("%SystemRoot%\\x"),
        RegistryData.MultiString(["a", "b"]),
        RegistryData.MultiString([]),
        RegistryData.Binary([0, 1, 255]),
    };

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void RegistryData_raw_roundtrip(RegistryData data)
    {
        var copy = RegistryData.FromRaw(data.Kind, data.ToRaw());

        copy.Should().Be(data);
        copy.GetHashCode().Should().Be(data.GetHashCode());
        copy.Display().Should().Be(data.Display());
    }

    [Fact]
    public void RegistryData_equality_respects_kind()
    {
        RegistryData.String("1").Should().NotBe(RegistryData.DWord(1));
        RegistryData.AreEqual(null, null).Should().BeTrue();
        RegistryData.AreEqual(RegistryData.DWord(1), null).Should().BeFalse();
        RegistryData.DWord(1).Equals((object)"x").Should().BeFalse();
    }

    [Fact]
    public void RegistryData_rejects_wrong_raw_length()
    {
        ((Action)(() => RegistryData.FromRaw(RegValueKind.DWord, [1, 2]))).Should().Throw<FormatException>();
        ((Action)(() => RegistryData.FromRaw(RegValueKind.QWord, [1, 2]))).Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData(RegValueKind.DWord, true)]
    [InlineData(RegValueKind.QWord, true)]
    [InlineData(RegValueKind.String, false)]
    public void RegLiteral_number_converts_only_to_numbers(RegValueKind kind, bool ok)
    {
        new RegLiteral { Number = 5 }.TryToData(kind, out _, out var error).Should().Be(ok);
        (error is null).Should().Be(ok);
    }

    [Fact]
    public void RegLiteral_conversions_and_matching()
    {
        new RegLiteral { Number = 5_000_000_000 }.TryToData(RegValueKind.DWord, out _, out _).Should().BeFalse();
        new RegLiteral { Text = "AAE=" }.ToData(RegValueKind.Binary).GetBytes().Should().Equal(0, 1);
        new RegLiteral { Text = "kein base64!" }.TryToData(RegValueKind.Binary, out _, out var error).Should().BeFalse();
        error.Should().Contain("Base64");
        new RegLiteral { Text = "x" }.ToData(RegValueKind.ExpandString).Kind.Should().Be(RegValueKind.ExpandString);
        new RegLiteral { Lines = ["a"] }.ToData(RegValueKind.MultiString).Lines.Should().Equal("a");
        ((Action)(() => new RegLiteral { Text = "x" }.ToData(RegValueKind.DWord))).Should().Throw<AscendedTweaksException>();

        new RegLiteral { Number = 0 }.Matches(RegistryData.DWord(0)).Should().BeTrue();
        new RegLiteral { Number = 0 }.Matches(RegistryData.String("0")).Should().BeFalse();
        new RegLiteral { Text = "506" }.Matches(RegistryData.String("506")).Should().BeTrue();
        new RegLiteral { Lines = ["a"] }.Matches(RegistryData.MultiString(["a"])).Should().BeTrue();
        new RegLiteral { Text = "AAE=" }.Matches(RegistryData.Binary([0, 1])).Should().BeTrue();
        new RegLiteral { Number = 0 }.Matches(null).Should().BeFalse();

        new RegLiteral { Lines = ["a"] }.SameAs(new RegLiteral { Lines = ["a"] }).Should().BeTrue();
        new RegLiteral { Number = 1 }.SameAs(new RegLiteral { Text = "1" }).Should().BeFalse();
        new RegLiteral { Number = 1 }.ToString().Should().Be("1");
        new RegLiteral { Lines = ["a", "b"] }.ToString().Should().Be("[a, b]");
    }

    [Theory]
    [InlineData(@"Software\A", true)]
    [InlineData(@"\Software\A", false)]
    [InlineData(@"Software\A\", false)]
    [InlineData(@"Software\..\A", false)]
    [InlineData(@"Software\\A", false)]
    [InlineData("Software/A", false)]
    [InlineData("", false)]
    [InlineData("Software\u0001A", false)]
    [InlineData(@"Software\ A", false)]
    public void RegistryPaths_validation(string path, bool expected)
    {
        RegistryPaths.IsWellFormed(path, out _).Should().Be(expected);
    }

    [Theory]
    [InlineData(RegHive.HKCU, @"Software\Classes\CLSID\{x}", true)]
    [InlineData(RegHive.HKCU, @"Software\Classes\CLSID", false)]
    [InlineData(RegHive.HKCU, @"Software\Microsoft\", false)]
    [InlineData(RegHive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\X", true)]
    [InlineData(RegHive.HKLM, @"SYSTEM\CurrentControlSet\Services\X", false)]
    [InlineData(RegHive.HKLM, @"Software\Classes\CLSID\{x}", false)]
    public void Key_allowlist(RegHive hive, string path, bool expected)
    {
        RegistryPaths.IsKeyOperationAllowed(hive, path).Should().Be(expected);
    }

    [Theory]
    [InlineData(19044, BuildSupport.Unsupported)]
    [InlineData(19045, BuildSupport.Supported)]
    [InlineData(22000, BuildSupport.Unsupported)]
    [InlineData(22631, BuildSupport.Supported)]
    [InlineData(26200, BuildSupport.Supported)]
    [InlineData(26201, BuildSupport.Untested)]
    public void SupportPolicy_classifies(int build, BuildSupport expected)
    {
        SupportPolicy.Classify(build).Should().Be(expected);
    }

    [Fact]
    public void Tweak_json_roundtrip_with_discriminators_in_any_order()
    {
        const string json = """
            {
              "id": "UI-900", "category": "Ui.Test", "nameKey": "k", "descriptionKey": "k", "sideEffectsKey": "k",
              "risk": "Safe", "evidence": "Proven", "builds": [ { "min": 22631, "max": null } ], "editions": [ "Home" ],
              "scope": "User", "requiresAdmin": false, "requiresReboot": false,
              "detect": { "path": "Software\\A", "type": "RegistryKeyExists", "hive": "HKCU" },
              "apply": [ { "hive": "HKCU", "path": "Software\\A", "type": "RegistryCreateKey" } ],
              "revert": [ { "type": "RegistryDeleteKey", "hive": "HKCU", "path": "Software\\A" } ],
              "sources": [ "x" ], "verifiedOn": [ { "build": 26200, "edition": "Home", "date": "2026-10-17", "method": "Host" } ],
              "hiddenByDefault": false
            }
            """;

        var tweak = JsonSerializer.Deserialize<TweakDefinition>(json, CoreJson.Options)!;
        var again = JsonSerializer.Deserialize<TweakDefinition>(JsonSerializer.Serialize(tweak, CoreJson.Options), CoreJson.Options)!;

        again.Detect.Should().BeOfType<RegistryKeyExistsCondition>();
        again.Apply.Single().Should().BeOfType<RegistryCreateKeyAction>();
        again.Revert.Actions!.Single().Should().BeOfType<RegistryDeleteKeyAction>();
        again.VerifiedOn.Single().Date.Should().Be(new DateOnly(2026, 10, 17));
        again.Module.Should().Be("UI");
        again.SupportsBuild(22631).Should().BeTrue();
        again.SupportsBuild(19045).Should().BeFalse();
    }

    [Theory]
    [InlineData("""{ "type": "FileClean" }""", "is not implemented in this version yet")]
    [InlineData("""{ "type": "Format-C" }""", "Unknown type")]
    [InlineData("""{ "hive": "HKCU" }""", "field 'type' is missing")]
    public void Action_json_errors_are_clear(string json, string message)
    {
        var act = () => JsonSerializer.Deserialize<TweakAction>(json, CoreJson.Options);

        act.Should().Throw<JsonException>().WithMessage($"*{message}*");
    }

    [Fact]
    public void Literal_and_revert_json_variants()
    {
        JsonSerializer.Deserialize<RegLiteral>("[\"a\",\"b\"]", CoreJson.Options)!.Lines.Should().Equal("a", "b");
        JsonSerializer.Serialize(new RegLiteral { Lines = ["a"] }, CoreJson.Options).Should().Contain("\"a\"");
        JsonSerializer.Serialize(new RegLiteral { Text = "t" }, CoreJson.Options).Should().Be("\"t\"");
        JsonSerializer.Serialize(RevertSpec.RestoreFromBackup, CoreJson.Options).Should().Be("\"restoreFromBackup\"");
        ((Action)(() => JsonSerializer.Deserialize<RegLiteral>("-1", CoreJson.Options))).Should().Throw<JsonException>();
        ((Action)(() => JsonSerializer.Deserialize<RegLiteral>("[1]", CoreJson.Options))).Should().Throw<JsonException>();
        ((Action)(() => JsonSerializer.Deserialize<RegLiteral>("true", CoreJson.Options))).Should().Throw<JsonException>();
        ((Action)(() => JsonSerializer.Deserialize<RevertSpec>("\"anders\"", CoreJson.Options))).Should().Throw<JsonException>();
    }

    [Fact]
    public void Command_whitelist_builds_arguments_only_from_allowed_values()
    {
        var definition = CommandWhitelist.All["fsutil.disablelastaccess"];

        definition.BuildSetArguments("1").Should().Equal("behavior", "set", "disablelastaccess", "1");
        ((Action)(() => definition.BuildSetArguments("1;x"))).Should().Throw<AscendedTweaksException>();
    }

    [Fact]
    public void Exception_constructors_keep_kind()
    {
        new AscendedTweaksException().Kind.Should().Be(ErrorKind.General);
        new AscendedTweaksException("m").Message.Should().Be("m");
        new AscendedTweaksException("m", new InvalidOperationException()).InnerException.Should().NotBeNull();
        new AscendedTweaksException(ErrorKind.Cancelled, "m", "X-001").TweakId.Should().Be("X-001");
    }

    [Fact]
    public void AllOf_leaves_are_flattened()
    {
        var leaf = new RegistryKeyExistsCondition { Hive = RegHive.HKCU, Path = "Software\\A" };
        var nested = new AllOfCondition { Conditions = [leaf, new AllOfCondition { Conditions = [leaf, leaf] }] };

        DetectTypeNames.LeavesOf(nested).Should().HaveCount(3);
        DetectTypeNames.NameOf(nested).Should().Be("AllOf");
    }

    [Fact]
    public void DataDirectory_takes_over_the_folder_of_the_earlier_name()
    {
        var root = Path.Combine(Path.GetTempPath(), "at-data-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ProductInfo.LegacyId, "State"));
            File.WriteAllText(Path.Combine(root, ProductInfo.LegacyId, "State", "ledger.json"), "{}");

            var moved = ProductInfo.DataDirectory(root);

            moved.Should().Be(Path.Combine(root, ProductInfo.Id));
            File.Exists(Path.Combine(moved, "State", "ledger.json")).Should().BeTrue();
            Directory.Exists(Path.Combine(root, ProductInfo.LegacyId)).Should().BeFalse();

            Directory.CreateDirectory(Path.Combine(root, ProductInfo.LegacyId));
            ProductInfo.DataDirectory(root).Should().Be(moved, "an existing new folder always wins");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DataDirectory_without_any_folder_is_the_new_name()
    {
        var root = Path.Combine(Path.GetTempPath(), "at-data-" + Guid.NewGuid().ToString("N"));
        ProductInfo.DataDirectory(root).Should().Be(Path.Combine(root, ProductInfo.Id));
        Directory.Exists(root).Should().BeFalse("only the path is returned, nothing is created");
    }
}
