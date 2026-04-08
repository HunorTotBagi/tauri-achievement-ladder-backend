using AchievementLadder.Configuration;
using AchievementLadder.Infrastructure;

namespace EndlessGuildExporter;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var projectRoot = ProjectPaths.FindProjectRoot(AppContext.BaseDirectory, "EndlessGuildExporter.csproj");
        var solutionRoot = ProjectPaths.FindSolutionRoot(projectRoot);
        var settingsPath = ResolveSettingsPath(projectRoot, solutionRoot);
        var outputPath = ParseOutputPath(args);

        using var cancellationTokenSource = new CancellationTokenSource();

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        try
        {
            var settings = AppSettings.Load(settingsPath);
            var exporter = new EndlessGuildExportService(solutionRoot, settings.TauriApi);

            Console.WriteLine("Exporting Endless guild members from Tauri to Excel...");

            var result = await exporter.ExportAsync(outputPath, cancellationTokenSource.Token);

            Console.WriteLine($"Rows written: {result.CharacterCount}");
            Console.WriteLine($"Character-sheet lookups: {result.CharacterSheetCount}");
            Console.WriteLine($"Guild-info fallbacks: {result.FallbackCount}");
            Console.WriteLine($"Workbook: {result.OutputPath}");

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Export cancelled.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Export failed: {ex.Message}");
            return 1;
        }
    }

    private static string? ParseOutputPath(IReadOnlyList<string> args)
    {
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (!string.Equals(argument, "--output", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(argument, "-o", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
            {
                throw new ArgumentException("Expected a file path after --output.");
            }

            return args[index + 1].Trim();
        }

        return null;
    }

    private static string ResolveSettingsPath(string projectRoot, string solutionRoot)
    {
        var sharedSettingsPath = Path.Combine(solutionRoot, "AchievementLadder", "appsettings.json");
        if (File.Exists(sharedSettingsPath))
        {
            return sharedSettingsPath;
        }

        var localSettingsPath = Path.Combine(projectRoot, "appsettings.json");
        if (File.Exists(localSettingsPath))
        {
            return localSettingsPath;
        }

        throw new FileNotFoundException(
            "Could not find appsettings.json in either AchievementLadder or EndlessGuildExporter.",
            sharedSettingsPath);
    }
}
