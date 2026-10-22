using System.Globalization;
using System.Text.Json;
using AscendedTweaks.Tools.PerfMeasure;

// Messprotokoll (Abschnitt 6): 5 Laeufe, Median. Entwicklungswerkzeug, wird nicht ausgeliefert.
//   perf-measure fps <label> <lauf1.csv> ... <lauf5.csv>     PresentMon-CSV je Lauf, Ausgabe: Median von Durchschnitts-FPS und 1-%-Tief
//   perf-measure values <label> <einheit> <wert1> ... <wert5>  beliebige Messwerte (zum Beispiel Startdauer aus Ereignis 100)
//   perf-measure compare <vorher.json> <nachher.json>          Differenz in Prozent und Einstufung nach Abschnitt 6
if (args.Length < 2)
{
    Console.Error.WriteLine("Aufruf: perf-measure fps <label> <csv>... | values <label> <einheit> <wert>... | compare <vorher.json> <nachher.json>");
    return 2;
}

try
{
    switch (args[0])
    {
        case "fps":
            var runs = args.Skip(2).Select(f => FrameStats.FromPresentMonCsv(File.ReadAllText(f))).ToList();
            var fps = Measurement.FromRuns(args[1], "fps", runs.Select(r => r.AverageFps).ToList(), runs.Select(r => r.OnePercentLowFps).ToList());
            Console.WriteLine(JsonSerializer.Serialize(fps, Measurement.Json));
            return 0;
        case "values":
            var values = args.Skip(3).Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToList();
            Console.WriteLine(JsonSerializer.Serialize(Measurement.FromRuns(args[1], args[2], values, null), Measurement.Json));
            return 0;
        case "compare":
            var before = JsonSerializer.Deserialize<Measurement>(File.ReadAllText(args[1]), Measurement.Json)!;
            var after = JsonSerializer.Deserialize<Measurement>(File.ReadAllText(args[2]), Measurement.Json)!;
            var comparison = Measurement.Compare(before, after);
            Console.WriteLine(JsonSerializer.Serialize(comparison, Measurement.Json));
            return 0;
        default:
            Console.Error.WriteLine($"Unbekannter Befehl {args[0]}.");
            return 2;
    }
}
catch (Exception ex) when (ex is IOException or FormatException or InvalidDataException or JsonException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
