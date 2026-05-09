using AchievementLadder.Configuration;
using AchievementLadder.Infrastructure;
using RealmFirstAchievements.Services;

namespace RealmFirstAchievements;

internal static class Program
{
    public static async Task<int> Main()
    {
        var projectRoot = ProjectPaths.FindProjectRoot(AppContext.BaseDirectory, "RealmFirstAchievements.csproj");
        var solutionRoot = ProjectPaths.FindSolutionRoot(projectRoot);
        var settingsPath = ResolveSettingsPath(projectRoot, solutionRoot);
        var frontendSourceDirectory = ProjectPaths.GetFrontendSrcDirectory(solutionRoot);

        using var cancellationTokenSource = new CancellationTokenSource();

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        try
        {
            var settings = AppSettings.Load(settingsPath);
            var exporter = new RealmFirstAchievementExportService(
                frontendSourceDirectory,
                settings.TauriApi);

            Console.WriteLine("Exporting achievement-firsts for Evermoon, Tauri, and WoD...");

            var result = await exporter.ExportAsync(cancellationTokenSource.Token);

            Console.WriteLine($"Candidate characters: {result.CandidateCharacterCount}");
            Console.WriteLine($"Valid characters: {result.ValidCharacterCount}");
            Console.WriteLine($"Frontend valid character file: {result.FrontendValidCharactersPath}");

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
