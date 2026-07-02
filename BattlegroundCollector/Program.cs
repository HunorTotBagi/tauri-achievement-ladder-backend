using AchievementLadder.Configuration;
using AchievementLadder.Infrastructure;

namespace BattlegroundCollector;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!BattlegroundCollectorOptions.TryParse(args, out var options, out var errorMessage, out var showHelp))
        {
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                Console.Error.WriteLine(errorMessage);
                Console.Error.WriteLine();
            }

            Console.WriteLine(BattlegroundCollectorOptions.UsageText);
            return showHelp ? 0 : 1;
        }

        var projectRoot = ProjectPaths.FindProjectRoot(AppContext.BaseDirectory, "BattlegroundCollector.csproj");
        var solutionRoot = ProjectPaths.FindSolutionRoot(projectRoot);
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
            var frontendSrcDirectory = ProjectPaths.GetFrontendSrcDirectory(solutionRoot);
            var collector = new BattlegroundCollectorService(projectRoot, solutionRoot, frontendSrcDirectory, settings.TauriApi);
            var result = await collector.ExecuteAsync(options!, cancellationTokenSource.Token);

            Console.WriteLine();
            Console.WriteLine($"Started at match id: {result.StartMatchId}");
            Console.WriteLine($"Next match id to try: {result.NextMatchId}");
            Console.WriteLine($"New battlegrounds: {result.NewBattlegroundCount}");
            Console.WriteLine($"New guilds: {result.NewGuildCount}");
            Console.WriteLine($"Total battlegrounds in JSON: {result.TotalBattlegroundCount}");
            Console.WriteLine($"Stop reason: {result.StopReason}");
            Console.WriteLine($"Output: {result.OutputPath}");
            Console.WriteLine($"State: {result.StatePath}");

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Battleground collection cancelled.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Battleground collection failed: {ex.Message}");
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
            "Could not find appsettings.json in either AchievementLadder or BattlegroundCollector.",
            sharedSettingsPath);
    }
}
