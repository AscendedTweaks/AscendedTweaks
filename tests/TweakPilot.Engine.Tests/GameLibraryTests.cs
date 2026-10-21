using FluentAssertions;
using TweakPilot.Engine.Insights;
using Xunit;

namespace TweakPilot.Engine.Tests;

public sealed class GameLibraryTests
{
    private const string Epic = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";
    private const string Steam = @"C:\Program Files (x86)\Steam";
    private readonly FakeFiles _fs = new();

    [Fact]
    public void Epic_and_steam_games_are_found_with_anti_cheat_and_launch_uri()
    {
        _fs.Dir(Epic);
        _fs.File(Epic + @"\a.item", """{"DisplayName":"Fortnite","AppName":"Fortnite","InstallLocation":"D:\\Epic\\Fortnite","LaunchExecutable":"FortniteGame/Binaries/Win64/FortniteLauncher.exe","MainGameAppName":""}""");
        _fs.File(Epic + @"\b.item", """{"DisplayName":"Fortnite Kreativmodus","AppName":"Mode","InstallLocation":"D:\\Epic\\Fortnite","MainGameAppName":"Fortnite"}""");
        _fs.File(Epic + @"\c.item", "kein json");
        _fs.File(Epic + @"\d.item", """{"DisplayName":"Entfernt","AppName":"Gone","InstallLocation":"D:\\Epic\\Gone","LaunchExecutable":"x.exe","MainGameAppName":""}""");
        _fs.Dir(@"D:\Epic\Fortnite");
        _fs.Dir(@"D:\Epic\Fortnite\FortniteGame");
        _fs.Dir(@"D:\Epic\Fortnite\FortniteGame\Binaries");
        _fs.Dir(@"D:\Epic\Fortnite\FortniteGame\Binaries\Win64");
        _fs.Dir(@"D:\Epic\Fortnite\FortniteGame\Binaries\Win64\EasyAntiCheat");
        _fs.File(@"D:\Epic\Fortnite\FortniteGame\Binaries\Win64\FortniteClient-Win64-Shipping.exe", string.Empty);

        _fs.File(Steam + @"\steamapps\libraryfolders.vdf", "\"libraryfolders\"\n{\n \"0\" { \"path\" \"C:\\\\Program Files (x86)\\\\Steam\" }\n \"1\" { \"path\" \"E:\\\\SteamLibrary\" }\n}");
        _fs.Dir(Steam + @"\steamapps");
        _fs.Dir(@"E:\SteamLibrary\steamapps");
        _fs.File(@"E:\SteamLibrary\steamapps\appmanifest_1172470.acf", "\"AppState\" { \"name\" \"Apex Legends\" \"installdir\" \"Apex Legends\" }");
        _fs.File(@"E:\SteamLibrary\steamapps\appmanifest_228980.acf", "\"AppState\" { \"name\" \"Steamworks Common Redistributables\" \"installdir\" \"Steamworks Shared\" }");
        _fs.File(@"E:\SteamLibrary\steamapps\appmanifest_1.acf", "kaputt");
        _fs.Dir(@"E:\SteamLibrary\steamapps\common\Apex Legends");
        _fs.Dir(@"E:\SteamLibrary\steamapps\common\Apex Legends\EasyAntiCheat");
        _fs.Dir(@"E:\SteamLibrary\steamapps\common\Apex Legends\BattlEye");

        var games = new GameLibrary(_fs, Epic, Steam).Scan();

        games.Select(g => g.Name).Should().Equal("Apex Legends", "Fortnite");
        var fortnite = games[1];
        fortnite.Source.Should().Be(GameSource.Epic);
        fortnite.ExePath.Should().EndWith("FortniteClient-Win64-Shipping.exe", "der Spielprozess, nicht der Starter");
        fortnite.LaunchUri.Should().Be("com.epicgames.launcher://apps/Fortnite?action=launch&silent=true");
        fortnite.AntiCheat.Should().Be(AntiCheat.EasyAntiCheat);
        var apex = games[0];
        apex.ExePath.Should().BeNull("Steam nennt keine EXE; der Nutzer waehlt sie");
        apex.LaunchUri.Should().Be("steam://rungameid/1172470");
        apex.AntiCheat.Should().Be(AntiCheat.EasyAntiCheat | AntiCheat.BattlEye);
    }

    [Fact]
    public void Missing_launchers_and_unreadable_folders_give_an_empty_list()
    {
        new GameLibrary(_fs, null, null).Scan().Should().BeEmpty();
        new GameLibrary(_fs, Epic, Steam).Scan().Should().BeEmpty();

        _fs.Dir(@"C:\Riot Games\VALORANT");
        _fs.Unreadable.Add(@"C:\Riot Games\VALORANT");
        new GameLibrary(_fs, null, null).DetectAntiCheat(@"C:\Riot Games\VALORANT").Should().Be(AntiCheat.Vanguard);
    }

    [Fact]
    public void Steam_without_library_file_uses_the_steam_folder()
    {
        _fs.Dir(Steam + @"\steamapps");
        _fs.File(Steam + @"\steamapps\appmanifest_730.acf", "\"AppState\" { \"name\" \"Counter-Strike 2\" \"installdir\" \"Counter-Strike Global Offensive\" }");
        _fs.Dir(Steam + @"\steamapps\common\Counter-Strike Global Offensive");

        new GameLibrary(_fs, null, Steam).Scan().Single().LaunchUri.Should().Be("steam://rungameid/730");
    }

    private sealed class FakeFiles : IGameFileSystem
    {
        private readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Unreadable { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Dir(string path) => _dirs.Add(path);

        public void File(string path, string text) => _files[path] = text;

        public bool DirectoryExists(string path) => _dirs.Contains(path);

        public bool FileExists(string path) => _files.ContainsKey(path);

        public IEnumerable<string> EnumerateFiles(string folder, string pattern)
        {
            var regex = new System.Text.RegularExpressions.Regex("^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal) + "$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return _files.Keys.Where(f => string.Equals(Path.GetDirectoryName(f), folder, StringComparison.OrdinalIgnoreCase) && regex.IsMatch(Path.GetFileName(f))).Order().ToList();
        }

        public IEnumerable<string> EnumerateDirectories(string folder) => Unreadable.Contains(folder)
            ? throw new UnauthorizedAccessException()
            : _dirs.Where(d => string.Equals(Path.GetDirectoryName(d), folder, StringComparison.OrdinalIgnoreCase)).ToList();

        public string ReadAllText(string path) => _files.TryGetValue(path, out var text) ? text : throw new FileNotFoundException(path);
    }
}
