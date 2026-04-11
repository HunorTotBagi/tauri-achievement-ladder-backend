using AchievementLadder.Configuration;
using AchievementLadder.Infrastructure;

namespace MissingPlayerFinder;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var projectRoot = ProjectPaths.FindProjectRoot(AppContext.BaseDirectory, "MissingPlayerFinder.csproj");
        var solutionRoot = ProjectPaths.FindSolutionRoot(projectRoot);
        var achievementLadderProjectRoot = Path.Combine(solutionRoot, "AchievementLadder");
        var settingsPath = ResolveSettingsPath(projectRoot, solutionRoot);

        using var cancellationTokenSource = new CancellationTokenSource();

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        try
        {
            var settings = AppSettings.Load(settingsPath);
            var finder = new MissingPlayerFinderService(
                solutionRoot,
                achievementLadderProjectRoot,
                settings.TauriApi);

            Console.WriteLine("Starting missing player backfill...");

            var result = await finder.GenerateAsync(cancellationTokenSource.Token);

            Console.WriteLine($"Scannable source characters: {result.SourceCharacterCount}");
            Console.WriteLine($"Players.csv characters before append: {result.CsvCharacterCount}");
            Console.WriteLine($"Missing characters at start: {result.MissingCharacterCount}");
            Console.WriteLine($"Characters appended: {result.AppendedCharacterCount}");
            Console.WriteLine($"Rare achievement entries merged: {result.RareAchievementEntryCount}");
            Console.WriteLine($"Characters still missing: {result.RemainingMissingCharacterCount}");
            Console.WriteLine($"Players.csv: {result.PlayersCsvPath}");
            if (result.RareAchievementsPath is not null)
            {
                Console.WriteLine($"RareAchievements.json: {result.RareAchievementsPath}");
            }
            if (result.LastUpdatedPath is not null)
            {
                Console.WriteLine($"lastUpdated.txt: {result.LastUpdatedPath}");
            }
            Console.WriteLine($"MissingPlayersToScan.txt: {result.MissingOutputPath}");

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Missing player scan cancelled.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Missing player scan failed: {ex.Message}");
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
            "Could not find appsettings.json in either AchievementLadder or MissingPlayerFinder.",
            sharedSettingsPath);
    }
}
