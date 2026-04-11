using AchievementLadder.Configuration;
using AchievementLadder.Infrastructure;
using AchievementLadder.Services;

namespace AchievementLadder;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var projectRoot = ProjectPaths.FindProjectRoot(AppContext.BaseDirectory, "AchievementLadder.csproj");
        var solutionRoot = ProjectPaths.FindSolutionRoot(projectRoot);
        var exportDirectory = ProjectPaths.GetFrontendSrcDirectory(solutionRoot);
        var settingsPath = Path.Combine(projectRoot, "appsettings.json");

        using var cancellationTokenSource = new CancellationTokenSource();

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        try
        {
            var settings = AppSettings.Load(settingsPath);
            var csvStore = new PlayerCsvStore(exportDirectory);
            var playerService = new PlayerService(projectRoot, settings.TauriApi, csvStore);

            Console.WriteLine("Starting player sync...");

            var result = await playerService.SyncDataAsync(cancellationTokenSource.Token);

            Console.WriteLine($"Generated {result.PlayerCount} player rows.");
            Console.WriteLine($"Characters needing retry: {result.RetryCharacterCount}");
            Console.WriteLine($"Players.csv: {result.PlayersCsvPath}");
            Console.WriteLine($"RareAchievements.json: {result.RareAchievementsPath}");
            Console.WriteLine($"lastUpdated.txt: {result.LastUpdatedPath}");
            Console.WriteLine($"MissingPlayersToScan.txt: {result.RetryOutputPath}");

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Sync cancelled.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Sync failed: {ex.Message}");
            return 1;
        }
    }
}
