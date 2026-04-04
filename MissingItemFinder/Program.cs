using AchievementLadder.Configuration;
using AchievementLadder.Infrastructure;

namespace MissingItemFinder;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!MissingItemFinderOptions.TryParse(args, out var options, out var errorMessage, out var showHelp))
        {
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                Console.Error.WriteLine(errorMessage);
                Console.Error.WriteLine();
            }

            Console.WriteLine(MissingItemFinderOptions.UsageText);
            return showHelp ? 0 : 1;
        }

        var projectRoot = ProjectPaths.FindProjectRoot(AppContext.BaseDirectory, "MissingItemFinder.csproj");
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
            var finder = new MissingItemFinderService(
                solutionRoot,
                projectRoot,
                achievementLadderProjectRoot,
                settings.TauriApi);

            var resolvedOptions = options!;
            Console.WriteLine(
                $"Starting item retry scan for {resolvedOptions.DescribeScope()} ({resolvedOptions.DescribeItems()})...");

            var result = await finder.ExecuteAsync(resolvedOptions, cancellationTokenSource.Token);

            Console.WriteLine($"Source characters: {result.SourceCharacterCount}");
            Console.WriteLine($"Scanned characters: {result.ScannedCharacterCount}");
            Console.WriteLine($"Characters with matches: {result.MatchedCharacterCount}");
            Console.WriteLine($"Characters still unresolved: {result.RemainingCharacterCount}");
            Console.WriteLine($"Item matches found: {result.ItemMatchCount}");
            Console.WriteLine($"Report: {result.OutputPath}");
            Console.WriteLine($"Retry file: {result.MissingOutputPath}");

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Missing item scan cancelled.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Missing item scan failed: {ex.Message}");
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
            "Could not find appsettings.json in either AchievementLadder or MissingItemFinder.",
            sharedSettingsPath);
    }
}
