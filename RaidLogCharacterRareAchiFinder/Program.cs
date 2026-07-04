using AchievementLadder.Configuration;
using AchievementLadder.Infrastructure;

namespace RaidLogCharacterRareAchiFinder;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!RaidLogFinderOptions.TryParse(args, out var options, out var errorMessage, out var showHelp))
        {
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                Console.Error.WriteLine(errorMessage);
                Console.Error.WriteLine();
            }

            Console.WriteLine(RaidLogFinderOptions.UsageText);
            return showHelp ? 0 : 1;
        }

        var projectRoot = ProjectPaths.FindProjectRoot(AppContext.BaseDirectory, "RaidLogCharacterRareAchiFinder.csproj");
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
            var finder = new RaidLogCharacterRareAchiFinderService(
                projectRoot,
                solutionRoot,
                settings.TauriApi);

            var result = await finder.ExecuteAsync(options!, cancellationTokenSource.Token);

            Console.WriteLine();
            Console.WriteLine($"Started at raid log id: {result.StartLogId}");
            Console.WriteLine($"Next raid log id to try: {result.NextLogId}");
            Console.WriteLine($"Raid logs scanned: {result.RaidLogCount}");
            Console.WriteLine($"Raid members found: {result.MemberCount}");
            Console.WriteLine($"Unique characters scanned: {result.ScannedCharacterCount}");
            Console.WriteLine($"Characters with matches: {result.MatchedCharacterCount}");
            Console.WriteLine($"Rare achievement matches: {result.AchievementMatchCount}");
            Console.WriteLine($"Rare item matches: {result.ItemMatchCount}");
            Console.WriteLine($"Rare mount matches: {result.MountMatchCount}");
            Console.WriteLine($"New guilds added: {result.NewGuildCount}");
            Console.WriteLine($"Stop reason: {result.StopReason}");
            Console.WriteLine($"State: {result.StatePath}");

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Raid log scan cancelled.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Raid log scan failed: {ex.Message}");
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
            "Could not find appsettings.json in either AchievementLadder or RaidLogCharacterRareAchiFinder.",
            sharedSettingsPath);
    }
}
