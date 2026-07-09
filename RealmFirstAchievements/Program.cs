using AchievementLadder.Configuration;
using AchievementLadder.Infrastructure;
using RealmFirstAchievements.Services;

namespace RealmFirstAchievements;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!RealmFirstAchievementExportOptions.TryParse(args, out var options, out var errorMessage, out var showHelp))
        {
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                Console.Error.WriteLine(errorMessage);
                Console.Error.WriteLine();
            }

            Console.WriteLine(RealmFirstAchievementExportOptions.UsageText);
            return showHelp ? 0 : 1;
        }

        var projectRoot = ProjectPaths.FindProjectRoot(AppContext.BaseDirectory, "RealmFirstAchievements.csproj");
        var solutionRoot = ProjectPaths.FindSolutionRoot(projectRoot);
        var settingsPath = ResolveSettingsPath(projectRoot, solutionRoot);
        var achievementLadderDataDirectory = Path.Combine(solutionRoot, "AchievementLadder", "Data");

        using var cancellationTokenSource = new CancellationTokenSource();

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        try
        {
            var settings = AppSettings.Load(settingsPath);
            if (options!.Parallelism is { } parallelism)
            {
                settings.TauriApi.MaxConcurrentRequests = parallelism;
            }

            var exporter = new RealmFirstAchievementExportService(
                achievementLadderDataDirectory,
                settings.TauriApi);

            Console.WriteLine("Exporting achievement-firsts for Evermoon, Tauri, and WoD...");
            Console.WriteLine($"API parallelism: {settings.TauriApi.MaxConcurrentRequests}");

            var result = await exporter.ExportAsync(cancellationTokenSource.Token);

            Console.WriteLine($"Candidate characters: {result.CandidateCharacterCount}");
            Console.WriteLine($"Valid characters: {result.ValidCharacterCount}");
            Console.WriteLine($"Valid character file: {result.ValidCharactersPath}");

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Realm first achievement export cancelled.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Realm first achievement export failed: {ex.Message}");
            return 1;
        }
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
            "Could not find appsettings.json in either AchievementLadder or RealmFirstAchievements.",
            sharedSettingsPath);
    }
}
