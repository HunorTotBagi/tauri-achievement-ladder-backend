using AchievementLadder.Configuration;
using AchievementLadder.Infrastructure;

namespace BattlegroundExtractor;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!ExtractorOptions.TryParse(args, out var options, out var errorMessage, out var showHelp))
        {
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                Console.Error.WriteLine(errorMessage);
                Console.Error.WriteLine();
            }

            Console.WriteLine(ExtractorOptions.UsageText);
            return showHelp ? 0 : 1;
        }

        var resolvedOptions = options!;
        var projectRoot = ProjectPaths.FindProjectRoot(AppContext.BaseDirectory, "BattlegroundExtractor.csproj");
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
            var service = new BattlegroundExtractorService(solutionRoot, settings.TauriApi);

            await service.ExecuteAsync(resolvedOptions, cancellationTokenSource.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Battleground extraction cancelled.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Battleground extraction failed: {ex.Message}");
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
            "Could not find appsettings.json in either AchievementLadder or BattlegroundExtractor.",
            sharedSettingsPath);
    }
}
