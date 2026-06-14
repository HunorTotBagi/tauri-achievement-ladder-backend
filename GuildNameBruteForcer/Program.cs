using AchievementLadder.Configuration;
using AchievementLadder.Infrastructure;

namespace GuildNameBruteForcer;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out var nameLength) || nameLength < 1 || nameLength > 10)
        {
            Console.Error.WriteLine("Usage: GuildNameBruteForcer <length> [realm]");
            Console.Error.WriteLine("  length  Number of characters to brute-force (1-10)");
            Console.Error.WriteLine("  realm   evermoon | tauri | wod  (default: all)");
            return 1;
        }

        string? realmFilter = args.Length >= 2 ? args[1].ToLowerInvariant().Trim() : null;
        if (realmFilter is not null && realmFilter is not ("evermoon" or "tauri" or "wod"))
        {
            Console.Error.WriteLine($"Unknown realm '{realmFilter}'. Valid values: evermoon, tauri, wod.");
            return 1;
        }

        var projectRoot = ProjectPaths.FindProjectRoot(AppContext.BaseDirectory, "GuildNameBruteForcer.csproj");
        var solutionRoot = ProjectPaths.FindSolutionRoot(projectRoot);
        var settingsPath = ResolveSettingsPath(solutionRoot, projectRoot);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            var settings = AppSettings.Load(settingsPath);
            var outputDir = Path.Combine(projectRoot, "Output");
            Directory.CreateDirectory(outputDir);

            Console.WriteLine($"Guild name length : {nameLength}");
            Console.WriteLine($"Realm(s)          : {realmFilter ?? "all"}");
            Console.WriteLine($"Output directory  : {outputDir}");
            Console.WriteLine();

            using var service = new GuildBruteForceService(settings.TauriApi);
            await service.ScanAsync(nameLength, realmFilter, outputDir, cts.Token);

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("\nScan cancelled — partial results saved.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Scan failed: {ex.Message}");
            return 1;
        }
    }

    private static string ResolveSettingsPath(string solutionRoot, string projectRoot)
    {
        var shared = Path.Combine(solutionRoot, "AchievementLadder", "appsettings.json");
        if (File.Exists(shared)) return shared;

        var local = Path.Combine(projectRoot, "appsettings.json");
        if (File.Exists(local)) return local;

        throw new FileNotFoundException("Could not find appsettings.json.", shared);
    }
}
