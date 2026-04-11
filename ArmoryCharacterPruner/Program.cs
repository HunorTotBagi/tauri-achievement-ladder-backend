using AchievementLadder.Configuration;
using AchievementLadder.Infrastructure;

namespace ArmoryCharacterPruner;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!ArmoryCharacterPrunerOptions.TryParse(args, out var options, out var errorMessage, out var showHelp))
        {
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                Console.Error.WriteLine(errorMessage);
                Console.Error.WriteLine();
            }

            Console.WriteLine(ArmoryCharacterPrunerOptions.UsageText);
            return showHelp ? 0 : 1;
        }

        var projectRoot = ProjectPaths.FindProjectRoot(AppContext.BaseDirectory, "ArmoryCharacterPruner.csproj");
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
            var pruner = new ArmoryCharacterPrunerService(
                solutionRoot,
                projectRoot,
                settings.TauriApi);

            var resolvedOptions = options!;
            Console.WriteLine(
                $"Starting armory prune for {resolvedOptions.DescribeScope()}...");

            var result = await pruner.PruneAsync(resolvedOptions, cancellationTokenSource.Token);

            Console.WriteLine($"Input file: {result.InputPath}");
            Console.WriteLine($"Output file: {result.OutputPath}");
            Console.WriteLine($"Total lines: {result.TotalLineCount}");
            Console.WriteLine($"Character rows checked: {result.CheckedCharacterRowCount}");
            Console.WriteLine($"Unique armory lookups: {result.UniqueCharacterCount}");
            Console.WriteLine($"Rows rewritten: {result.RewrittenRowCount}");
            Console.WriteLine($"Rows removed: {result.RemovedRowCount}");
            Console.WriteLine($"Rows kept: {result.KeptRowCount}");
            Console.WriteLine($"Unparsed rows kept: {result.UnparsedRowCount}");
            Console.WriteLine($"Lookup failures kept: {result.FailedLookupCount}");

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Armory prune cancelled.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Armory prune failed: {ex.Message}");
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
            "Could not find appsettings.json in either AchievementLadder or ArmoryCharacterPruner.",
            sharedSettingsPath);
    }
}
