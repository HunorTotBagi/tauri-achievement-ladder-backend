using AchievementLadder.Configuration;
using AchievementLadder.Infrastructure;

namespace GuildCharacterExporter;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var projectRoot = ProjectPaths.FindProjectRoot(AppContext.BaseDirectory, "GuildCharacterExporter.csproj");
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
            var exporter = new GuildCharacterExportService(solutionRoot, settings.TauriApi);

            Console.WriteLine("Starting guild character export...");

            var result = await exporter.ExportAsync(cancellationTokenSource.Token);

            Console.WriteLine($"Generated {result.CharacterCount} character rows.");
            Console.WriteLine($"GuildCharacters.txt: {result.OutputPath}");

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Export cancelled.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Export failed: {ex.Message}");
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
            "Could not find appsettings.json in either AchievementLadder or GuildCharacterExporter.",
            sharedSettingsPath);
    }
}
