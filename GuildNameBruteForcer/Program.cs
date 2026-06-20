using AchievementLadder.Configuration;
using AchievementLadder.Infrastructure;

namespace GuildNameBruteForcer;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!GuildScanOptions.TryParse(args, out var options, out var errorMessage, out var showHelp))
        {
            Console.Error.WriteLine(errorMessage);
            Console.Error.WriteLine();
            Console.Error.WriteLine(GuildScanOptions.UsageText);
            return 1;
        }

        if (showHelp)
        {
            Console.WriteLine(GuildScanOptions.UsageText);
            return 0;
        }

        var parsedOptions = options!;
        var projectRoot = ProjectPaths.FindProjectRoot(AppContext.BaseDirectory, "GuildNameBruteForcer.csproj");
        var solutionRoot = ProjectPaths.FindSolutionRoot(projectRoot);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        try
        {
            IReadOnlyList<string>? dictionaryCandidates = null;
            string? dictionaryPath = null;
            DictionarySource? dictionarySource = null;

            if (parsedOptions.Mode == GuildScanMode.Dictionary)
            {
                dictionarySource = DictionaryWordList.GetSource(parsedOptions.DictionaryLanguage);
                var usesDefaultDictionary = string.IsNullOrWhiteSpace(parsedOptions.DictionaryPath);
                dictionaryPath = usesDefaultDictionary
                    ? DictionaryWordList.GetDefaultPath(projectRoot, dictionarySource)
                    : ResolveInputPath(projectRoot, parsedOptions.DictionaryPath!);

                if (usesDefaultDictionary)
                {
                    await DictionaryWordList.EnsureDefaultFileExistsAsync(
                        dictionaryPath,
                        dictionarySource,
                        cts.Token);
                }

                dictionaryCandidates = DictionaryWordList.LoadCandidates(
                    dictionaryPath,
                    parsedOptions.MinLength,
                    parsedOptions.MaxLength);

                Console.WriteLine($"Language            : {dictionarySource.DisplayName}");
                Console.WriteLine($"Dictionary          : {dictionaryPath}");
                Console.WriteLine($"Candidate lengths   : {parsedOptions.MinLength}-{parsedOptions.MaxLength}");
                Console.WriteLine($"Unique candidates   : {dictionaryCandidates.Count:N0}");
                Console.WriteLine($"Realm(s)            : {parsedOptions.RealmFilter ?? "all"}");

                if (parsedOptions.CountOnly)
                {
                    return 0;
                }
            }

            var settingsPath = ResolveSettingsPath(solutionRoot, projectRoot);
            var settings = AppSettings.Load(settingsPath);
            var outputDirectory = Path.Combine(projectRoot, "Output");
            Directory.CreateDirectory(outputDirectory);

            using var service = new GuildBruteForceService(settings.TauriApi);

            if (parsedOptions.Mode == GuildScanMode.Dictionary)
            {
                Console.WriteLine($"Output directory    : {outputDirectory}");
                Console.WriteLine();

                await service.ScanDictionaryAsync(
                    dictionaryCandidates!,
                    dictionarySource!.Key,
                    parsedOptions.RealmFilter,
                    outputDirectory,
                    cts.Token);
            }
            else
            {
                Console.WriteLine($"Guild name length   : {parsedOptions.NameLength}");
                Console.WriteLine($"Realm(s)            : {parsedOptions.RealmFilter ?? "all"}");
                Console.WriteLine($"Output directory    : {outputDirectory}");
                Console.WriteLine();

                await service.ScanAsync(
                    parsedOptions.NameLength!.Value,
                    parsedOptions.RealmFilter,
                    outputDirectory,
                    cts.Token);
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("\nScan cancelled — completed dictionary candidates were checkpointed.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Scan failed: {ex.Message}");
            return 1;
        }
    }

    private static string ResolveInputPath(string projectRoot, string inputPath) =>
        Path.GetFullPath(Path.IsPathRooted(inputPath)
            ? inputPath
            : Path.Combine(projectRoot, inputPath));

    private static string ResolveSettingsPath(string solutionRoot, string projectRoot)
    {
        var shared = Path.Combine(solutionRoot, "AchievementLadder", "appsettings.json");
        if (File.Exists(shared)) return shared;

        var local = Path.Combine(projectRoot, "appsettings.json");
        if (File.Exists(local)) return local;

        throw new FileNotFoundException("Could not find appsettings.json.", shared);
    }
}
