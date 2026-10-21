using System.Text.Json.Nodes;
using FluentAssertions;
using TweakPilot.Core;
using Xunit;

namespace TweakPilot.Catalog.Tests;

public sealed class CatalogTests
{
    private const string ValidTweak = """
        {
          "id": "PRIV-900",
          "category": "Privacy.Test",
          "nameKey": "tweak.PRIV-900.name",
          "descriptionKey": "tweak.PRIV-900.desc",
          "sideEffectsKey": "tweak.PRIV-900.side",
          "risk": "Safe",
          "evidence": "Proven",
          "builds": [ { "min": 19045, "max": null } ],
          "editions": [ "Home", "Pro" ],
          "scope": "User",
          "requiresAdmin": false,
          "requiresReboot": false,
          "detect": { "type": "RegistryValueEquals", "hive": "HKCU", "path": "Software\\Test", "name": "X", "value": 0 },
          "apply": [ { "type": "RegistrySetValue", "hive": "HKCU", "path": "Software\\Test", "name": "X", "kind": "DWord", "value": 0 } ],
          "revert": "restoreFromBackup",
          "sources": [ "Testquelle mit ausreichender Laenge" ],
          "verifiedOn": [],
          "hiddenByDefault": false,
          "impact": "Small",
          "impactNote": "tweak.PRIV-900.impact",
          "measuredOn": [],
          "plainTitleKey": "tweak.PRIV-900.plain.title",
          "plainDescKey": "tweak.PRIV-900.plain.desc"
        }
        """;

    private static readonly string Schema = ReadSchema();

    [Fact]
    public void Embedded_catalog_loads_all_phase1_tweaks_without_findings()
    {
        var catalog = CatalogLoader.LoadEmbedded();

        catalog.Tweaks.Select(t => t.Id).Should().Contain(["PERF-001", "PERF-016", "PRIV-001", "PRIV-013", "UI-001"]);
        catalog.Tweaks.Select(t => t.Id).Should().OnlyHaveUniqueItems();
        catalog.Text("de-DE", "tweak.PRIV-001.name").Should().Be("Werbe-ID ausschalten");
        catalog.Text("en-US", "tweak.PRIV-001.name").Should().Be("Turn off advertising ID");
        catalog.Text("fr-FR", "x").Should().Be("[x]");
        catalog.TryGet("priv-001", out var tweak).Should().BeTrue();
        tweak.Detect.Should().BeOfType<RegistryValueEqualsCondition>();
    }

    [Fact]
    public void Source_directory_matches_embedded_catalog()
    {
        var result = CatalogLoader.LoadFromDirectory(FindCatalogDirectory());

        result.Issues.Should().BeEmpty();
        result.Catalog!.Tweaks.Should().HaveCount(CatalogLoader.LoadEmbedded().Tweaks.Count);
    }

    [Fact]
    public void Disputed_tweaks_are_hidden_by_default()
    {
        CatalogLoader.LoadEmbedded().Tweaks
            .Where(t => t.Evidence == EvidenceLevel.Disputed)
            .Should().OnlyContain(t => t.HiddenByDefault);
    }

    [Fact]
    public void Valid_tweak_has_no_findings()
    {
        Load(ValidTweak).Issues.Should().BeEmpty();
    }

    [Theory]
    [InlineData("sources", "schema")]
    [InlineData("nameKey", "schema")]
    [InlineData("verifiedOn", "schema")]
    [InlineData("detect", "schema")]
    public void Missing_required_field_fails_schema(string field, string rule)
    {
        var node = JsonNode.Parse(ValidTweak)!.AsObject();
        node.Remove(field);

        Load(node.ToJsonString()).Issues.Should().Contain(i => i.Rule == rule);
    }

    [Fact]
    public void Unknown_action_type_fails_schema()
    {
        Load(Mutate(n => n["apply"]![0]!["type"] = "RunScript")).Issues.Should().Contain(i => i.Rule == "schema");
    }

    [Fact]
    public void Apply_value_must_match_detect()
    {
        var result = Load(Mutate(n => n["apply"]![0]!["value"] = 1));

        result.Issues.Should().Contain(i => i.Rule == "apply-detect");
    }

    [Fact]
    public void Detect_condition_without_action_is_reported()
    {
        var result = Load(Mutate(n => n["detect"] = JsonNode.Parse("""
            { "type": "AllOf", "conditions": [
              { "type": "RegistryValueEquals", "hive": "HKCU", "path": "Software\\Test", "name": "X", "value": 0 },
              { "type": "RegistryValueEquals", "hive": "HKCU", "path": "Software\\Test", "name": "Y", "value": 0 } ] }
            """)));

        result.Issues.Should().Contain(i => i.Rule == "apply-detect" && i.Message.Contains("has no matching action"));
    }

    [Fact]
    public void Duplicate_ids_are_reported()
    {
        var files = new[] { new CatalogSourceFile("PRIV-900.json", ValidTweak), new CatalogSourceFile("x/PRIV-900.json", ValidTweak) };

        CatalogLoader.Load(files, Texts(), Schema).Issues.Should().Contain(i => i.Rule == "unique-id");
    }

    [Fact]
    public void Missing_translation_is_reported()
    {
        var texts = Texts();
        texts["en-US"] = "{}";

        CatalogLoader.Load([new CatalogSourceFile("PRIV-900.json", ValidTweak)], texts, Schema)
            .Issues.Should().Contain(i => i.Rule == "text-missing" && i.Message.Contains("en-US"));
    }

    [Fact]
    public void Missing_language_file_is_reported()
    {
        var texts = Texts();
        texts.Remove("de-DE");

        CatalogLoader.Load([new CatalogSourceFile("PRIV-900.json", ValidTweak)], texts, Schema)
            .Issues.Should().Contain(i => i.Rule == "i18n");
    }

    [Fact]
    public void Impact_is_required_and_noticeable_needs_a_measurement()
    {
        Load(Mutate(n => n.Remove("impact"))).Issues.Should().NotBeEmpty("Schema verlangt das Feld");
        Load(Mutate(n => n["impact"] = "Noticeable")).Issues.Should().Contain(i => i.Rule == "impact" && i.Message.Contains("needs at least one measurement"));
        Load(Mutate(n =>
        {
            n["impact"] = "Noticeable";
            n["measuredOn"] = new JsonArray("Microsoft Learn: Beleg fuer das Verhalten");
        })).Issues.Should().BeEmpty();
    }

    [Fact]
    public void Disputed_is_unclear_and_unclear_is_expert_only()
    {
        Load(Mutate(n =>
        {
            n["evidence"] = "Disputed";
            n["hiddenByDefault"] = true;
        })).Issues.Should().Contain(i => i.Rule == "impact" && i.Message.Contains("Unclear"));
        Load(Mutate(n => n["impact"] = "Unclear")).Issues.Should().Contain(i => i.Rule == "impact" && i.Message.Contains("expert mode"));
    }

    [Theory]
    [InlineData("Ein Titel mit viel zu vielen Woertern", "Kurz.", "more than 5 words")]
    [InlineData("Kurz", "Dieser Satz ist absichtlich viel zu lang, damit er die Grenze von neunzig Zeichen ueberschreitet.", "longer than 90 characters")]
    [InlineData("", "Kurz.", "Title is missing")]
    public void Plain_texts_respect_the_simple_mode_limits(string title, string description, string expected)
    {
        var texts = Texts();
        texts["en-US"] = texts["en-US"].Replace("\"Kurzer Titel\"", System.Text.Json.JsonSerializer.Serialize(title), StringComparison.Ordinal)
            .Replace("\"Ein kurzer Satz.\"", System.Text.Json.JsonSerializer.Serialize(description), StringComparison.Ordinal);

        CatalogLoader.Load([new CatalogSourceFile("PRIV-900.json", ValidTweak)], texts, Schema).Issues
            .Should().Contain(i => i.Rule == "plain-text" && i.Message.Contains("en-US") && i.Message.Contains(expected));
    }

    [Fact]
    public void Generated_tweaks_have_impact_and_plain_texts_in_both_languages()
    {
        var catalog = CatalogLoader.LoadEmbedded();
        var composite = new CompositeCatalog(catalog);
        var ids = new[] { "PERF-026/HKCU-RUN/Programm", @"GAME-007/C:\Spiele\x.exe", @"GAME-008/2/C:\Spiele\x.exe", "GAME-016/x.exe", "NET-004/1.1.1.1", UpdateTweaks.PauseIdFor(DateOnly.FromDateTime(DateTime.UtcNow), 7), "UPD-007/11/25H2" };
        foreach (var id in ids)
        {
            composite.TryGet(id, out var tweak).Should().BeTrue(id);
            tweak.Impact.Should().NotBeNull(id);
            (tweak.Impact == ImpactLevel.Unclear).Should().Be(tweak.HiddenByDefault, id);
            foreach (var culture in CatalogLinter.RequiredCultures)
            {
                var title = catalog.Text(culture, tweak.PlainTitleKey!);
                var description = catalog.Text(culture, tweak.PlainDescKey!);
                CatalogLinter.PlainTextProblems(title, description).Should().BeEmpty($"{id} {culture}");
                catalog.Text(culture, tweak.ImpactNoteKey!).Should().NotBe(tweak.ImpactNoteKey, $"{id} {culture}");
            }
        }
    }

    [Fact]
    public void Disputed_must_be_hidden()
    {
        Load(Mutate(n => n["evidence"] = "Disputed")).Issues.Should().Contain(i => i.Rule == "hidden");
    }

    [Fact]
    public void Hklm_action_in_user_scope_is_rejected()
    {
        var result = Load(Mutate(n =>
        {
            n["apply"]![0]!["hive"] = "HKLM";
            n["detect"]!["hive"] = "HKLM";
        }));

        result.Issues.Should().Contain(i => i.Rule == "scope");
    }

    [Fact]
    public void Hkcu_policy_path_requires_user_policy_scope_and_admin()
    {
        var policy = Mutate(n =>
        {
            n["apply"]![0]!["path"] = "Software\\Policies\\Test";
            n["detect"]!["path"] = "Software\\Policies\\Test";
        });
        Load(policy).Issues.Should().Contain(i => i.Rule == "scope");

        var fixedPolicy = JsonNode.Parse(policy)!.AsObject();
        fixedPolicy["scope"] = "UserPolicy";
        Load(fixedPolicy.ToJsonString()).Issues.Should().Contain(i => i.Rule == "admin");
        fixedPolicy["requiresAdmin"] = true;
        Load(fixedPolicy.ToJsonString()).Issues.Should().BeEmpty();
    }

    [Fact]
    public void Scheduled_task_must_be_a_windows_task_in_machine_scope()
    {
        var task = Mutate(n =>
        {
            n["scope"] = "Machine";
            n["requiresAdmin"] = true;
            n["apply"] = JsonNode.Parse("""[ { "type": "ScheduledTaskSetEnabled", "taskPath": "\\Microsoft\\Windows\\Test\\Aufgabe", "enabled": false } ]""");
            n["detect"] = JsonNode.Parse("""{ "type": "ScheduledTaskEnabledEquals", "taskPath": "\\Microsoft\\Windows\\Test\\Aufgabe", "value": false }""");
        });
        Load(task).Issues.Should().BeEmpty();

        var traversal = task.Replace("Test\\\\Aufgabe", "..\\\\Aufgabe", StringComparison.Ordinal);
        Load(traversal).Issues.Should().Contain(i => i.Rule == "task-path");

        var userScope = JsonNode.Parse(task)!.AsObject();
        userScope["scope"] = "User";
        userScope["requiresAdmin"] = false;
        Load(userScope.ToJsonString()).Issues.Should().Contain(i => i.Rule == "scope");
    }

    [Fact]
    public void System_parameter_must_be_whitelisted_user_scope_and_in_range()
    {
        var spi = Mutate(n =>
        {
            n["apply"] = JsonNode.Parse("""[ { "type": "SystemParametersInfo", "parameter": "MenuShowDelay", "value": 100 } ]""");
            n["detect"] = JsonNode.Parse("""{ "type": "SystemParameterEquals", "parameter": "MenuShowDelay", "value": 100 }""");
        });
        Load(spi).Issues.Should().BeEmpty();
        Load(spi.Replace("100", "9000", StringComparison.Ordinal)).Issues.Should().Contain(i => i.Rule == "parameter");
        Load(spi.Replace("\"value\":100}]", "\"value\":0}]", StringComparison.Ordinal)).Issues.Should().Contain(i => i.Rule == "apply-detect");
    }

    [Fact]
    public void Machine_scope_requires_admin()
    {
        Load(Mutate(n => n["scope"] = "Machine")).Issues.Should().Contain(i => i.Rule == "admin");
    }

    [Fact]
    public void Delete_key_outside_allowlist_is_rejected()
    {
        var result = Load(Mutate(n => n["revert"] = JsonNode.Parse("""[ { "type": "RegistryDeleteKey", "hive": "HKCU", "path": "Software\\Fremd" } ]""")));

        result.Issues.Should().Contain(i => i.Rule == "key-allowlist");
    }

    [Fact]
    public void Path_traversal_is_rejected()
    {
        var result = Load(Mutate(n =>
        {
            n["apply"]![0]!["path"] = "Software\\..\\Test";
            n["detect"]!["path"] = "Software\\..\\Test";
        }));

        result.Issues.Should().Contain(i => i.Rule == "registry-path");
    }

    [Fact]
    public void Command_must_be_whitelisted_with_allowed_value()
    {
        var command = Mutate(n =>
        {
            n["scope"] = "Machine";
            n["requiresAdmin"] = true;
            n["apply"] = JsonNode.Parse("""[ { "type": "Command", "command": "fsutil.disablelastaccess", "value": "7" } ]""");
            n["detect"] = JsonNode.Parse("""{ "type": "CommandQueryEquals", "command": "fsutil.disablelastaccess", "value": "7" }""");
        });

        Load(command).Issues.Should().Contain(i => i.Rule == "command" && i.Message.Contains("is not allowed"));
    }

    [Fact]
    public void Wrong_category_and_text_keys_are_reported()
    {
        var result = Load(Mutate(n =>
        {
            n["category"] = "Gaming.Test";
            n["nameKey"] = "tweak.PRIV-901.name";
        }));

        result.Issues.Should().Contain(i => i.Rule == "category");
        result.Issues.Should().Contain(i => i.Rule == "text-key");
    }

    [Fact]
    public void File_name_must_match_id()
    {
        CatalogLoader.Load([new CatalogSourceFile("PRIV-999.json", ValidTweak)], Texts(), Schema)
            .Issues.Should().Contain(i => i.Rule == "file-name");
    }

    [Fact]
    public void Broken_json_is_reported()
    {
        Load("{ nicht json").Issues.Should().Contain(i => i.Rule == "json");
    }

    [Fact]
    public void Placebo_is_allowed_for_documentation_but_never_shipped()
    {
        var result = Load(Mutate(n => n["evidence"] = "Placebo"));

        result.Issues.Should().BeEmpty();
        result.Catalog!.Tweaks.Should().BeEmpty();
        result.Catalog.AllIncludingPlacebo.Should().ContainSingle();
    }

    [Fact]
    public void Empty_build_range_is_reported()
    {
        Load(Mutate(n => n["builds"]![0]!["max"] = 19000)).Issues.Should().Contain(i => i.Rule == "builds");
    }

    private static CatalogLoadResult Load(string json) => CatalogLoader.Load([new CatalogSourceFile("PRIV-900.json", json)], Texts(), Schema);

    private static string Mutate(Action<JsonObject> change)
    {
        var node = JsonNode.Parse(ValidTweak)!.AsObject();
        change(node);
        return node.ToJsonString();
    }

    private static Dictionary<string, string> Texts()
    {
        const string texts = """{ "tweak.PRIV-900.name": "n", "tweak.PRIV-900.desc": "d", "tweak.PRIV-900.side": "s", "tweak.PRIV-900.impact": "i", "tweak.PRIV-900.plain.title": "Kurzer Titel", "tweak.PRIV-900.plain.desc": "Ein kurzer Satz." }""";
        return new Dictionary<string, string> { ["de-DE"] = texts, ["en-US"] = texts };
    }

    private static string ReadSchema() => File.ReadAllText(Path.Combine(FindCatalogDirectory(), "tweak.schema.json"));

    private static string FindCatalogDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "TweakPilot.Catalog");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("src/TweakPilot.Catalog nicht gefunden.");
    }
}
