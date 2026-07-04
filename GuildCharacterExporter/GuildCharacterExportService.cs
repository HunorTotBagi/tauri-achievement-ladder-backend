using System.Text;
using System.Text.Json;
using AchievementLadder.Configuration;
using AchievementLadder.Dtos;
using AchievementLadder.Infrastructure;

namespace GuildCharacterExporter;

public sealed class GuildCharacterExportService(string solutionRoot, TauriApiOptions apiOptions)
{
    private const int ProgressInterval = 25;

    private static readonly RealmSource[] RealmSources =
    [
        new("evermoon-guilds.txt", "[EN] Evermoon", "Evermoon"),
        new("tauri-guilds.txt", "[HU] Tauri WoW Server", "Tauri"),
        new("wod-guilds.txt", "[HU] Warriors of Darkness", "WoD")
    ];

    private readonly string _solutionRoot = Path.GetFullPath(solutionRoot);
    private readonly TauriApiOptions _apiOptions = apiOptions;

    public async Task<GuildCharacterExportResult> ExportAsync(CancellationToken cancellationToken)
    {
        var guildDataDirectory = Path.Combine(_solutionRoot, "AchievementLadder", "Data", "Guilds");
        if (!Directory.Exists(guildDataDirectory))
        {
            throw new DirectoryNotFoundException($"Could not find guild data folder: {guildDataDirectory}");
        }

        var guilds = LoadGuilds(guildDataDirectory)
            .DistinctBy(guild => (guild.GuildName.ToLowerInvariant(), guild.ApiRealm))
            .ToList();

        Console.WriteLine($"Scanning {guilds.Count} guilds...");
        Console.WriteLine(
            $"API settings: concurrency={_apiOptions.MaxConcurrentRequests}, timeout={_apiOptions.RequestTimeoutSeconds}s, retries={_apiOptions.MaxRetryAttempts}");

        var characterLines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var retryGuilds = new List<GuildSource>();
        var processedGuildCount = 0;

        using var apiClient = new TauriApiClient(_apiOptions);

        foreach (var guild in guilds)
        {
            var result = await LoadGuildMembersAsync(apiClient, guild, cancellationToken);
            if (result.Succeeded)
            {
                foreach (var memberName in result.Members)
                {
                    characterLines.Add($"{memberName}-{guild.DisplayRealm}");
                }
            }
            else
            {
                retryGuilds.Add(guild);
            }

            processedGuildCount++;
            if (processedGuildCount % ProgressInterval == 0 || processedGuildCount == guilds.Count)
            {
                Console.WriteLine($"Loaded guilds {processedGuildCount}/{guilds.Count}");
            }
        }

        var outputPath = Path.Combine(
            _solutionRoot,
            "AchievementLadder",
            "Data",
            "GuildCharacters",
            "GuildCharacters.txt");
        var retryOutputPath = Path.Combine(_solutionRoot, "MissingGuildsToScan.txt");
        var orderedRetryGuilds = retryGuilds
            .OrderBy(guild => guild.DisplayRealm, StringComparer.OrdinalIgnoreCase)
            .ThenBy(guild => guild.GuildName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await WriteRetryGuildsAsync(retryOutputPath, orderedRetryGuilds, cancellationToken);

        if (orderedRetryGuilds.Count > 0)
        {
            throw new InvalidOperationException(
                $"Could not load {orderedRetryGuilds.Count} of {guilds.Count} guilds after configured retries. " +
                $"MissingGuildsToScan.txt: {retryOutputPath}. GuildCharacters.txt was not overwritten.");
        }

        var orderedLines = characterLines
            .OrderBy(line => line, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await WriteLinesAsync(outputPath, orderedLines, cancellationToken);

        return new GuildCharacterExportResult(
            guilds.Count,
            orderedLines.Count,
            orderedRetryGuilds.Count,
            outputPath,
            retryOutputPath);
    }

    private static IEnumerable<GuildSource> LoadGuilds(string guildDataDirectory)
    {
        foreach (var source in RealmSources)
        {
            var path = Path.Combine(guildDataDirectory, source.FileName);
            if (!File.Exists(path))
            {
                continue;
            }

            foreach (var rawLine in File.ReadLines(path))
            {
                var guildName = rawLine?.Trim();
                if (string.IsNullOrWhiteSpace(guildName))
                {
                    continue;
                }

                yield return new GuildSource(guildName, source.ApiRealm, source.DisplayRealm);
            }
        }
    }

    private static async Task<GuildLoadResult> LoadGuildMembersAsync(
        TauriApiClient apiClient,
        GuildSource guild,
        CancellationToken cancellationToken)
    {
        var result = await apiClient.FetchResponseElementAsync(
            "guild-info",
            new
            {
                r = guild.ApiRealm,
                gn = guild.GuildName
            },
            $"guild '{guild.GuildName}' on {guild.DisplayRealm}",
            cancellationToken);

        if (!result.Succeeded || result.ResponseElement is not { } response)
        {
            return GuildLoadResult.Failure();
        }

        if (response.ValueKind != JsonValueKind.Object ||
            !response.TryGetProperty("guildList", out var guildListElement) ||
            guildListElement.ValueKind != JsonValueKind.Object)
        {
            Console.Error.WriteLine(
                $"Could not load guild '{guild.GuildName}' on {guild.DisplayRealm}: response did not contain a guildList object.");
            return GuildLoadResult.Failure();
        }

        try
        {
            var guildInfo = response.Deserialize<GuildInfoInner>();
            if (guildInfo?.guildList is null)
            {
                Console.Error.WriteLine(
                    $"Could not load guild '{guild.GuildName}' on {guild.DisplayRealm}: response did not contain guild members.");
                return GuildLoadResult.Failure();
            }

            var members = guildInfo.guildList.Values
                .Where(member => member.level >= 70)
                .Select(member => member.name?.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .ToList();

            return GuildLoadResult.Success(members);
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine(
                $"Could not parse guild '{guild.GuildName}' on {guild.DisplayRealm}: {ex.Message}");
            return GuildLoadResult.Failure();
        }
    }

    private static async Task WriteLinesAsync(string path, IReadOnlyList<string> lines, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
        await using (var writer = new StreamWriter(stream, encoding))
        {
            foreach (var line in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(line);
            }
        }

        File.Move(tempPath, path, overwrite: true);
    }

    private static async Task WriteRetryGuildsAsync(
        string path,
        IReadOnlyList<GuildSource> guilds,
        CancellationToken cancellationToken)
    {
        var lines = guilds
            .Select(guild => $"{guild.DisplayRealm}\t{guild.GuildName}")
            .ToList();

        await WriteLinesAsync(path, lines, cancellationToken);
    }

    private sealed record RealmSource(string FileName, string ApiRealm, string DisplayRealm);

    private sealed record GuildSource(string GuildName, string ApiRealm, string DisplayRealm);

    private readonly record struct GuildLoadResult(bool Succeeded, IReadOnlyList<string> Members)
    {
        public static GuildLoadResult Success(IReadOnlyList<string> members) => new(true, members);

        public static GuildLoadResult Failure() => new(false, Array.Empty<string>());
    }
}
