using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Tauri.Core.Configuration;
using Tauri.Core.Dtos;
using Tauri.Core.Infrastructure;

namespace GuildCharacterExporter;

public sealed class GuildCharacterExportService(string solutionRoot, TauriApiOptions apiOptions)
{
    private const int ProgressInterval = 25;
    private const string RetryFileName = "MissingGuildsToScan.txt";

    private static readonly RealmSource[] RealmSources =
    [
        new("evermoon-guilds.txt", "[EN] Evermoon", "Evermoon"),
        new("tauri-guilds.txt", "[HU] Tauri WoW Server", "Tauri"),
        new("wod-guilds.txt", "[HU] Warriors of Darkness", "WoD"),
    ];

    private readonly string _solutionRoot = Path.GetFullPath(solutionRoot);
    private readonly TauriApiOptions _apiOptions = apiOptions;
    private readonly int _guildWorkerCount = Math.Max(4, apiOptions.MaxConcurrentRequests * 2);

    public async Task<GuildCharacterExportResult> ExportAsync(CancellationToken cancellationToken)
    {
        var guildDataDirectory = Path.Combine(_solutionRoot, "AchievementLadder", "Data", "Guilds");
        if (!Directory.Exists(guildDataDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Could not find guild data folder: {guildDataDirectory}"
            );
        }

        var outputPath = Path.Combine(
            _solutionRoot,
            "AchievementLadder",
            "Data",
            "GuildCharacters",
            "GuildCharacters.txt"
        );
        var retryOutputPath = Path.Combine(_solutionRoot, RetryFileName);
        var retryInputGuilds = LoadRetryGuilds(retryOutputPath)
            .DistinctBy(guild => (guild.GuildName.ToLowerInvariant(), guild.ApiRealm))
            .ToList();
        var usedRetryInput = retryInputGuilds.Count > 0;
        var guilds = usedRetryInput
            ? retryInputGuilds
            : LoadGuilds(guildDataDirectory)
                .DistinctBy(guild => (guild.GuildName.ToLowerInvariant(), guild.ApiRealm))
                .ToList();

        if (usedRetryInput)
        {
            Console.WriteLine(
                $"Retry file found with {guilds.Count} guilds. Scanning only {RetryFileName} entries."
            );
        }
        else if (File.Exists(retryOutputPath))
        {
            Console.WriteLine(
                $"{RetryFileName} has no guilds to retry. Running a full guild scan."
            );
        }

        Console.WriteLine($"Scanning {guilds.Count} guilds...");
        Console.WriteLine(
            $"API settings: concurrency={_apiOptions.MaxConcurrentRequests}, timeout={_apiOptions.RequestTimeoutSeconds}s, retries={_apiOptions.MaxRetryAttempts}"
        );

        var existingCharacterLines = usedRetryInput
            ? LoadExistingCharacterLines(outputPath)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var characterLines = new ConcurrentDictionary<string, byte>(
            existingCharacterLines.Select(line => new KeyValuePair<string, byte>(line, 0)),
            StringComparer.OrdinalIgnoreCase
        );
        if (usedRetryInput)
        {
            Console.WriteLine(
                $"Loaded {characterLines.Count} existing character rows to merge retry results."
            );
        }

        var retryGuilds = new ConcurrentBag<GuildSource>();
        var processedGuildCount = 0;
        var progressLock = new Lock();

        using var apiClient = new TauriApiClient(_apiOptions);

        await Parallel.ForEachAsync(
            guilds,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _guildWorkerCount,
                CancellationToken = cancellationToken,
            },
            async (guild, ct) =>
            {
                var result = await LoadGuildMembersAsync(apiClient, guild, ct);
                if (result.Succeeded)
                {
                    foreach (var memberName in result.Members)
                    {
                        characterLines.TryAdd($"{memberName}-{guild.DisplayRealm}", 0);
                    }
                }
                else
                {
                    retryGuilds.Add(guild);
                }

                var processed = Interlocked.Increment(ref processedGuildCount);
                if (processed % ProgressInterval == 0 || processed == guilds.Count)
                {
                    lock (progressLock)
                    {
                        Console.WriteLine($"Loaded guilds {processed}/{guilds.Count}");
                    }
                }
            }
        );

        var orderedRetryGuilds = retryGuilds
            .OrderBy(guild => guild.DisplayRealm, StringComparer.OrdinalIgnoreCase)
            .ThenBy(guild => guild.GuildName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await WriteRetryGuildsAsync(retryOutputPath, orderedRetryGuilds, cancellationToken);

        var orderedLines = characterLines.Keys
            .OrderBy(line => line, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await WriteLinesAsync(outputPath, orderedLines, cancellationToken);

        return new GuildCharacterExportResult(
            guilds.Count,
            orderedLines.Count,
            orderedRetryGuilds.Count,
            outputPath,
            retryOutputPath,
            usedRetryInput
        );
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

    private static IEnumerable<GuildSource> LoadRetryGuilds(string path)
    {
        if (!File.Exists(path))
        {
            yield break;
        }

        var lineNumber = 0;
        foreach (var rawLine in File.ReadLines(path, Encoding.UTF8))
        {
            lineNumber++;

            var line = rawLine?.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split('\t', 2, StringSplitOptions.TrimEntries);
            if (
                parts.Length != 2
                || string.IsNullOrWhiteSpace(parts[0])
                || string.IsNullOrWhiteSpace(parts[1])
            )
            {
                throw new InvalidDataException(
                    $"Could not parse {Path.GetFileName(path)} line {lineNumber}. Expected format: Realm<TAB>GuildName."
                );
            }

            var source = ResolveRealmSource(parts[0]);
            if (source is null)
            {
                var knownRealms = string.Join(
                    ", ",
                    RealmSources.Select(realmSource => realmSource.DisplayRealm)
                );
                throw new InvalidDataException(
                    $"Could not parse {Path.GetFileName(path)} line {lineNumber}: unknown realm '{parts[0]}'. Known realms: {knownRealms}."
                );
            }

            yield return new GuildSource(parts[1], source.ApiRealm, source.DisplayRealm);
        }
    }

    private static RealmSource? ResolveRealmSource(string realmName)
    {
        return RealmSources.FirstOrDefault(source =>
            string.Equals(source.DisplayRealm, realmName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(source.ApiRealm, realmName, StringComparison.OrdinalIgnoreCase)
        );
    }

    private static HashSet<string> LoadExistingCharacterLines(string path)
    {
        var lines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
        {
            return lines;
        }

        foreach (var rawLine in File.ReadLines(path, Encoding.UTF8))
        {
            var line = rawLine?.Trim();
            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line);
            }
        }

        return lines;
    }

    private static async Task<GuildLoadResult> LoadGuildMembersAsync(
        TauriApiClient apiClient,
        GuildSource guild,
        CancellationToken cancellationToken
    )
    {
        var result = await apiClient.FetchResponseElementAsync(
            "guild-info",
            new { r = guild.ApiRealm, gn = guild.GuildName },
            $"guild '{guild.GuildName}' on {guild.DisplayRealm}",
            cancellationToken
        );

        if (!result.Succeeded || result.ResponseElement is not { } response)
        {
            return GuildLoadResult.Failure();
        }

        if (
            response.ValueKind != JsonValueKind.Object
            || !response.TryGetProperty("guildList", out var guildListElement)
            || guildListElement.ValueKind != JsonValueKind.Object
        )
        {
            Console.Error.WriteLine(
                $"Could not load guild '{guild.GuildName}' on {guild.DisplayRealm}: response did not contain a guildList object."
            );
            return GuildLoadResult.Failure();
        }

        try
        {
            var guildInfo = response.Deserialize<GuildInfoInner>();
            if (guildInfo?.guildList is null)
            {
                Console.Error.WriteLine(
                    $"Could not load guild '{guild.GuildName}' on {guild.DisplayRealm}: response did not contain guild members."
                );
                return GuildLoadResult.Failure();
            }

            var members = guildInfo
                .guildList.Values.Where(member => member.level >= 70)
                .Select(member => member.name?.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .ToList();

            return GuildLoadResult.Success(members);
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine(
                $"Could not parse guild '{guild.GuildName}' on {guild.DisplayRealm}: {ex.Message}"
            );
            return GuildLoadResult.Failure();
        }
    }

    private static async Task WriteLinesAsync(
        string path,
        IReadOnlyList<string> lines,
        CancellationToken cancellationToken
    )
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        await using (
            var stream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                useAsync: true
            )
        )
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
        CancellationToken cancellationToken
    )
    {
        var lines = guilds.Select(guild => $"{guild.DisplayRealm}\t{guild.GuildName}").ToList();

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
