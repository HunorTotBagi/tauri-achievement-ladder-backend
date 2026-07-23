using Tauri.Core.Configuration;
using Tauri.Core.Infrastructure;

namespace Guildkukker;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            PrintUsage();
            return 0;
        }

        if (!TryParseArguments(args, out var arguments))
        {
            Console.Error.WriteLine("Expected a realm name and a guild name.");
            PrintUsage();
            return 2;
        }

        var projectRoot = ProjectPaths.FindProjectRoot(
            AppContext.BaseDirectory,
            "Guildkukker.csproj"
        );
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
            using var apiClient = new TauriApiClient(settings.TauriApi);
            var outputDirectory = arguments.OutputDirectory is null
                ? projectRoot
                : Path.GetFullPath(arguments.OutputDirectory.Trim().Trim('"'));
            var exporter = new GuildMemberExportService(outputDirectory, apiClient);

            var result = await exporter.ExportAsync(
                arguments.RealmName,
                arguments.GuildName,
                cancellationTokenSource.Token
            );

            Console.WriteLine($"Scanned {result.ScannedPlayerCount} guild players.");
            Console.WriteLine($"Exported {result.PlayerCount} level 110 players.");
            Console.WriteLine($"Nightfallen reputations found: {result.ReputationCount}");
            Console.WriteLine($"Missing reputation data: {result.MissingReputationCount}");
            Console.WriteLine($"Text file: {result.OutputPath}");
            Console.WriteLine($"Spreadsheet: {result.SpreadsheetOutputPath}");
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

    private static void PrintUsage()
    {
        Console.WriteLine(
            "Usage: dotnet run --project Guildkukker -- <realm> <guild> [--output-directory <folder>]"
        );
        Console.WriteLine("Example: dotnet run --project Guildkukker -- Evermoon Endless");
    }

    private static bool TryParseArguments(
        IReadOnlyList<string> args,
        out CommandArguments arguments
    )
    {
        arguments = new CommandArguments(string.Empty, string.Empty, null);

        if (args.Count < 2)
        {
            return false;
        }

        var nameParts = new List<string>();
        string? outputDirectory = null;

        for (var index = 0; index < args.Count; index++)
        {
            if (
                args[index] is not "--output-directory"
                && args[index] is not "--output-dir"
                && args[index] is not "-o"
            )
            {
                nameParts.Add(args[index]);
                continue;
            }

            if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
            {
                return false;
            }

            outputDirectory = args[++index].Trim();
        }

        var realmPartCount = nameParts[0].StartsWith("[", StringComparison.Ordinal) ? 2 : 1;
        if (nameParts.Count <= realmPartCount)
        {
            return false;
        }

        var realmName = string.Join(' ', nameParts.Take(realmPartCount)).Trim();
        var guildName = string.Join(' ', nameParts.Skip(realmPartCount)).Trim();
        arguments = new CommandArguments(realmName, guildName, outputDirectory);
        return !string.IsNullOrWhiteSpace(realmName) && !string.IsNullOrWhiteSpace(guildName);
    }

    private static string ResolveSettingsPath(string projectRoot, string solutionRoot)
    {
        var sharedSettingsPath = Path.Combine(
            solutionRoot,
            "AchievementLadder",
            "appsettings.json"
        );
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
            "Could not find appsettings.json in either AchievementLadder or Guildkukker.",
            sharedSettingsPath
        );
    }

    private sealed record CommandArguments(
        string RealmName,
        string GuildName,
        string? OutputDirectory
    );
}
