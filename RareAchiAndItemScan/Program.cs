using AchievementLadder.Configuration;
using AchievementLadder.Infrastructure;

namespace RareAchiAndItemScan;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!ScanOptions.TryParse(args, out var options, out var errorMessage, out var showHelp))
        {
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                Console.Error.WriteLine(errorMessage);
                Console.Error.WriteLine();
            }

            Console.WriteLine(ScanOptions.UsageText);
            return showHelp ? 0 : 1;
        }

        var projectRoot = ProjectPaths.FindProjectRoot(AppContext.BaseDirectory, "RareAchiAndItemScan.csproj");
        var solutionRoot = ProjectPaths.FindSolutionRoot(projectRoot);
        var achievementLadderProjectRoot = Path.Combine(solutionRoot, "AchievementLadder");
        var settingsPath = ResolveSettingsPath(projectRoot, solutionRoot);
        var resolvedOptions = options!;

        using var cancellationTokenSource = new CancellationTokenSource();

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        try
        {
            var settings = AppSettings.Load(settingsPath);
            var scanner = new RareAchiAndItemScanService(
                projectRoot,
                achievementLadderProjectRoot,
                settings.TauriApi);

            Console.WriteLine(
                $"Starting rare scan for {resolvedOptions.DescribeScope()} ({resolvedOptions.DescribeTargets()})...");

            var result = await scanner.ExecuteAsync(resolvedOptions, cancellationTokenSource.Token);

            Console.WriteLine($"Scanned characters: {result.ScannedCharacterCount}");
            Console.WriteLine($"Characters with matches: {result.MatchedCharacterCount}");
            Console.WriteLine($"Failed characters: {result.FailedCharacterCount}");
            Console.WriteLine($"Rare achievement matches: {result.AchievementMatchCount}");
            Console.WriteLine($"Rare item matches: {result.ItemMatchCount}");
            Console.WriteLine($"Rare mount matches: {result.MountMatchCount}");
            Console.WriteLine($"Report: {result.OutputPath}");

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Rare scan cancelled.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Rare scan failed: {ex.Message}");
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
            "Could not find appsettings.json in either AchievementLadder or RareAchiAndItemScan.",
            sharedSettingsPath);
    }
}
