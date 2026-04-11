using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AchievementLadder.Configuration;
using AchievementLadder.Helpers;
using AchievementLadder.Infrastructure;
using AchievementLadder.Models;
using RareAchiAndItemScan;

namespace MissingPlayerFinder;

public sealed class MissingPlayerFinderService(
    string solutionRoot,
    string achievementLadderProjectRoot,
    TauriApiOptions apiOptions)
{
    private static readonly CharacterKeyComparer CharacterComparer = new();
    private static readonly JsonSerializerOptions FrontendJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
    private static readonly IReadOnlyList<RareAchievementDefinition> RareAchievementDefinitions = RareScanCatalog.RareAchievementNames
        .Select(entry => new RareAchievementDefinition(entry.Key, entry.Value))
        .ToList();
    private const int ProgressInterval = 100;

    private readonly string _solutionRoot = Path.GetFullPath(solutionRoot);
    private readonly string _achievementLadderProjectRoot = Path.GetFullPath(achievementLadderProjectRoot);
    private readonly TauriApiOptions _apiOptions = apiOptions;
    private readonly int _characterWorkerCount = Math.Max(4, apiOptions.MaxConcurrentRequests * 2);

    public async Task<MissingPlayerFinderResult> GenerateAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_achievementLadderProjectRoot))
        {
            throw new DirectoryNotFoundException(
                $"Could not find AchievementLadder project folder: {_achievementLadderProjectRoot}");
        }

        var frontendSrcDirectory = ProjectPaths.GetFrontendSrcDirectory(_solutionRoot);
        var playersCsvPath = Path.Combine(frontendSrcDirectory, "Players.csv");
        var rareAchievementsPath = Path.Combine(frontendSrcDirectory, "RareAchievements.json");
        var lastUpdatedPath = Path.Combine(frontendSrcDirectory, "lastUpdated.txt");
        var retryOutputPath = Path.Combine(_solutionRoot, "MissingPlayersToScan.txt");

        if (!File.Exists(playersCsvPath))
        {
            throw new FileNotFoundException($"Could not find Players.csv at {playersCsvPath}", playersCsvPath);
        }

        var sourceCharacters = LoadSourceCharacters(cancellationToken);
        var csvPlayers = LoadCsvPlayers(playersCsvPath, cancellationToken);
        var retryCharacters = LoadRetryCharacters(retryOutputPath, cancellationToken);
        var targets = BuildBackfillTargets(sourceCharacters, csvPlayers, retryCharacters);

        Console.WriteLine($"Missing characters at start: {targets.Count}");
        Console.WriteLine(
            $"API settings: concurrency={_apiOptions.MaxConcurrentRequests}, timeout={_apiOptions.RequestTimeoutSeconds}s, retries={_apiOptions.MaxRetryAttempts}");

        var playersToAppend = new ConcurrentBag<Player>();
        var rareAchievementEntries = new ConcurrentBag<CharacterRareAchievementEntry>();
        var unresolvedCharacters = new ConcurrentBag<CharacterToScan>();
        var processedCount = 0;

        using var apiClient = new TauriApiClient(_apiOptions);

        await Parallel.ForEachAsync(
            targets,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _characterWorkerCount,
                CancellationToken = cancellationToken
            },
            async (target, ct) =>
            {
                var result = await FetchBackfillAsync(apiClient, target, ct);

                if (target.RequiresPlayerBackfill && result.Player is { } fetchedPlayer)
                {
                    playersToAppend.Add(fetchedPlayer);
                }

                if (target.RequiresRareAchievementBackfill &&
                    result.RareAchievementsSucceeded &&
                    result.RareAchievements.Count > 0)
                {
                    var metadataPlayer = result.Player ?? GetExistingCsvPlayer(csvPlayers, target.Character);
                    if (metadataPlayer is not null)
                    {
                        rareAchievementEntries.Add(new CharacterRareAchievementEntry(
                            metadataPlayer.Name,
                            metadataPlayer.Realm,
                            metadataPlayer.Race,
                            metadataPlayer.Gender,
                            metadataPlayer.Class,
                            metadataPlayer.Guild,
                            result.RareAchievements));
                    }
                }

                if (!result.IsFullySuccessful)
                {
                    unresolvedCharacters.Add(target.Character);
                }

                var processed = Interlocked.Increment(ref processedCount);
                if (processed % ProgressInterval == 0 || processed == targets.Count)
                {
                    Console.Write($"\rBackfill progress: {processed}/{targets.Count}");
                }
            });

        if (targets.Count > 0)
        {
            Console.WriteLine();
        }

        var orderedPlayers = playersToAppend
            .OrderByDescending(player => player.AchievementPoints)
            .ThenByDescending(player => player.HonorableKills)
            .ThenBy(player => player.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(player => player.Realm, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var orderedRareEntries = rareAchievementEntries
            .Distinct(new RareAchievementEntryComparer())
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Realm, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var remainingCharacters = unresolvedCharacters
            .Distinct(new CharacterToScanComparer())
            .OrderBy(character => character.DisplayRealm, StringComparer.OrdinalIgnoreCase)
            .ThenBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await AppendPlayersAsync(playersCsvPath, orderedPlayers, cancellationToken);
        var updatedRareAchievementsPath = await MergeRareAchievementsAsync(
            rareAchievementsPath,
            orderedRareEntries,
            cancellationToken);
        var refreshedLastUpdatedPath = await RefreshLastUpdatedAsync(
            lastUpdatedPath,
            orderedPlayers.Count > 0 || updatedRareAchievementsPath is not null,
            cancellationToken);

        await WriteMissingCharactersAsync(retryOutputPath, remainingCharacters, cancellationToken);

        return new MissingPlayerFinderResult(
            sourceCharacters.Count,
            csvPlayers.Count,
            targets.Count,
            orderedPlayers.Count,
            orderedRareEntries.Count,
            remainingCharacters.Count,
            playersCsvPath,
            updatedRareAchievementsPath,
            refreshedLastUpdatedPath,
            retryOutputPath);
    }

    private HashSet<CharacterToScan> LoadSourceCharacters(CancellationToken cancellationToken)
    {
        var allCharacters = new List<(string Name, string ApiRealm, string DisplayRealm)>();

        CharacterHelpers.LoadDefaultCharacterSources(
            _achievementLadderProjectRoot,
            allCharacters,
            includePvPSeasonCharacters: true);

        var result = new HashSet<CharacterToScan>(new CharacterToScanComparer());

        foreach (var character in allCharacters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(character.Name) ||
                character.Name.Contains('#', StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(character.DisplayRealm))
            {
                continue;
            }

            result.Add(new CharacterToScan(
                character.Name.Trim(),
                character.ApiRealm.Trim(),
                character.DisplayRealm.Trim()));
        }

        return result;
    }

    private Dictionary<CharacterKey, Player> LoadCsvPlayers(string playersCsvPath, CancellationToken cancellationToken)
    {
        using var lines = File.ReadLines(playersCsvPath).GetEnumerator();
        if (!lines.MoveNext())
        {
            return new Dictionary<CharacterKey, Player>(CharacterComparer);
        }

        var header = ParseCsvLine(lines.Current);
        var nameIndex = FindColumnIndex(header, "Name");
        var realmIndex = FindColumnIndex(header, "Realm");

        if (nameIndex < 0 || realmIndex < 0)
        {
            throw new InvalidDataException("Players.csv must contain Name and Realm columns.");
        }

        var raceIndex = FindColumnIndex(header, "Race");
        var genderIndex = FindColumnIndex(header, "Gender");
        var classIndex = FindColumnIndex(header, "Class");
        var guildIndex = FindColumnIndex(header, "Guild");
        var achievementPointsIndex = FindColumnIndex(header, "AchievementPoints");
        var honorableKillsIndex = FindColumnIndex(header, "HonorableKills");
        var factionIndex = FindColumnIndex(header, "Faction");

        var result = new Dictionary<CharacterKey, Player>(CharacterComparer);

        while (lines.MoveNext())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = lines.Current;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var values = ParseCsvLine(line);
            if (nameIndex >= values.Count || realmIndex >= values.Count)
            {
                continue;
            }

            var name = values[nameIndex].Trim();
            var realm = values[realmIndex].Trim();

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(realm))
            {
                continue;
            }

            var player = new Player
            {
                Name = name,
                Realm = realm,
                Race = ReadIntValue(values, raceIndex),
                Gender = ReadIntValue(values, genderIndex),
                Class = ReadIntValue(values, classIndex),
                Guild = ReadStringValue(values, guildIndex),
                AchievementPoints = ReadIntValue(values, achievementPointsIndex),
                HonorableKills = ReadIntValue(values, honorableKillsIndex),
                Faction = ReadStringValue(values, factionIndex)
            };

            result[new CharacterKey(name, realm)] = player;
        }

        return result;
    }

    private List<BackfillTarget> BuildBackfillTargets(
        HashSet<CharacterToScan> sourceCharacters,
        Dictionary<CharacterKey, Player> csvPlayers,
        IReadOnlyList<CharacterToScan> retryCharacters)
    {
        var targetMap = new Dictionary<CharacterToScan, ScanRequirements>(new CharacterToScanComparer());

        foreach (var character in sourceCharacters)
        {
            if (!csvPlayers.ContainsKey(new CharacterKey(character.Name, character.DisplayRealm)))
            {
                UpsertTarget(character, requiresPlayerBackfill: true, requiresRareAchievementBackfill: true);
            }
        }

        foreach (var retryCharacter in retryCharacters)
        {
            var requiresPlayerBackfill = !csvPlayers.ContainsKey(new CharacterKey(retryCharacter.Name, retryCharacter.DisplayRealm));
            UpsertTarget(retryCharacter, requiresPlayerBackfill, requiresRareAchievementBackfill: true);
        }

        return targetMap
            .Select(entry => new BackfillTarget(
                entry.Key,
                entry.Value.RequiresPlayerBackfill,
                entry.Value.RequiresRareAchievementBackfill))
            .OrderBy(target => target.Character.DisplayRealm, StringComparer.OrdinalIgnoreCase)
            .ThenBy(target => target.Character.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        void UpsertTarget(
            CharacterToScan character,
            bool requiresPlayerBackfill,
            bool requiresRareAchievementBackfill)
        {
            if (!targetMap.TryGetValue(character, out var requirements))
            {
                requirements = new ScanRequirements();
                targetMap[character] = requirements;
            }

            requirements.RequiresPlayerBackfill |= requiresPlayerBackfill;
            requirements.RequiresRareAchievementBackfill |= requiresRareAchievementBackfill;
        }
    }

    private static List<CharacterToScan> LoadRetryCharacters(string retryOutputPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(retryOutputPath))
        {
            return [];
        }

        var result = new List<CharacterToScan>();

        foreach (var line in File.ReadLines(retryOutputPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!CharacterHelpers.TryExtractCharacterWithRealm(line, out var name, out var apiRealm, out var displayRealm))
            {
                continue;
            }

            result.Add(new CharacterToScan(name, apiRealm, displayRealm));
        }

        return result;
    }

    private static async Task<CharacterBackfillResult> FetchBackfillAsync(
        TauriApiClient apiClient,
        BackfillTarget target,
        CancellationToken cancellationToken)
    {
        var playerTask = target.RequiresPlayerBackfill
            ? FetchPlayerAsync(apiClient, target.Character, cancellationToken)
            : Task.FromResult(PlayerFetchResult.Skipped());

        var rareAchievementTask = target.RequiresRareAchievementBackfill
            ? FetchRareAchievementsAsync(apiClient, target.Character, cancellationToken)
            : Task.FromResult(RareAchievementFetchResult.Skipped());

        await Task.WhenAll(playerTask, rareAchievementTask);

        var playerResult = await playerTask;
        var rareAchievementResult = await rareAchievementTask;

        return new CharacterBackfillResult(
            playerResult.Player,
            playerResult.Succeeded,
            rareAchievementResult.Achievements,
            rareAchievementResult.Succeeded);
    }

    private static async Task<PlayerFetchResult> FetchPlayerAsync(
        TauriApiClient apiClient,
        CharacterToScan character,
        CancellationToken cancellationToken)
    {
        var responseResult = await apiClient.FetchResponseElementAsync(
            "character-sheet",
            new
            {
                r = character.ApiRealm,
                n = character.Name
            },
            $"{character.Name}-{character.DisplayRealm}",
            cancellationToken);

        if (!responseResult.Succeeded || responseResult.ResponseElement is not { } response)
        {
            return PlayerFetchResult.Failure();
        }

        int race = response.TryGetProperty("race", out var value) ? value.GetInt32() : 0;
        int gender = response.TryGetProperty("gender", out value) ? value.GetInt32() : 0;
        int @class = response.TryGetProperty("class", out value) ? value.GetInt32() : 0;
        int achievementPoints = response.TryGetProperty("pts", out value) ? value.GetInt32() : 0;
        int honorableKills = response.TryGetProperty("playerHonorKills", out value) ? value.GetInt32() : 0;
        string faction = response.TryGetProperty("faction_string_class", out value)
            ? (value.GetString() ?? string.Empty)
            : string.Empty;
        string guild = response.TryGetProperty("guildName", out value)
            ? (value.GetString() ?? string.Empty)
            : string.Empty;

        return PlayerFetchResult.Success(new Player
        {
            Name = character.Name,
            Race = race,
            Gender = gender,
            Class = @class,
            Realm = character.DisplayRealm,
            Guild = guild,
            AchievementPoints = achievementPoints,
            HonorableKills = honorableKills,
            Faction = faction
        });
    }

    private static async Task<RareAchievementFetchResult> FetchRareAchievementsAsync(
        TauriApiClient apiClient,
        CharacterToScan character,
        CancellationToken cancellationToken)
    {
        var responseResult = await apiClient.FetchResponseElementAsync(
            "character-achievements",
            new
            {
                r = character.ApiRealm,
                n = character.Name
            },
            $"{character.Name}-{character.DisplayRealm}",
            cancellationToken);

        if (!responseResult.Succeeded || responseResult.ResponseElement is not { } response)
        {
            return RareAchievementFetchResult.Failure();
        }

        return RareAchievementFetchResult.Success(
            RareAchievementExtractor.ExtractRareAchievements(response, RareAchievementDefinitions));
    }

    private static Player? GetExistingCsvPlayer(
        Dictionary<CharacterKey, Player> csvPlayers,
        CharacterToScan character)
    {
        csvPlayers.TryGetValue(new CharacterKey(character.Name, character.DisplayRealm), out var player);
        return player;
    }

    private static async Task AppendPlayersAsync(
        string playersCsvPath,
        IReadOnlyList<Player> players,
        CancellationToken cancellationToken)
    {
        if (players.Count == 0)
        {
            return;
        }

        var directory = Path.GetDirectoryName(playersCsvPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var writeHeader = !File.Exists(playersCsvPath) || new FileInfo(playersCsvPath).Length == 0;
        var needsLeadingNewLine = !writeHeader && NeedsLeadingNewLine(playersCsvPath);

        await using var stream = new FileStream(playersCsvPath, FileMode.Append, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
        await using var writer = new StreamWriter(stream, encoding);

        if (writeHeader)
        {
            await writer.WriteLineAsync("\"Name\",\"Race\",\"Gender\",\"Class\",\"Realm\",\"Guild\",\"AchievementPoints\",\"HonorableKills\",\"Faction\"");
        }
        else if (needsLeadingNewLine)
        {
            await writer.WriteLineAsync();
        }

        foreach (var player in players)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(BuildCsvLine(player));
        }
    }

    private static async Task<string?> MergeRareAchievementsAsync(
        string rareAchievementsPath,
        IReadOnlyList<CharacterRareAchievementEntry> entries,
        CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
        {
            return null;
        }

        RareAchievementExport existingExport;
        if (File.Exists(rareAchievementsPath))
        {
            await using var readStream = new FileStream(rareAchievementsPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
            existingExport = await JsonSerializer.DeserializeAsync<RareAchievementExport>(
                                 readStream,
                                 FrontendJsonOptions,
                                 cancellationToken)
                             ?? throw new InvalidDataException($"Could not deserialize RareAchievements.json at {rareAchievementsPath}.");
        }
        else
        {
            existingExport = new RareAchievementExport(
                DateTimeOffset.UtcNow,
                RareAchievementDefinitions,
                []);
        }

        var entryKeys = entries
            .Select(entry => new CharacterKey(entry.Name, entry.Realm))
            .ToHashSet(CharacterComparer);

        var mergedCharacters = existingExport.Characters
            .Where(entry => !entryKeys.Contains(new CharacterKey(entry.Name, entry.Realm)))
            .Concat(entries)
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Realm, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var updatedExport = new RareAchievementExport(
            DateTimeOffset.UtcNow,
            RareAchievementDefinitions,
            mergedCharacters);

        var directory = Path.GetDirectoryName(rareAchievementsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = rareAchievementsPath + ".tmp";
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await JsonSerializer.SerializeAsync(stream, updatedExport, FrontendJsonOptions, cancellationToken);
        }

        File.Move(tempPath, rareAchievementsPath, overwrite: true);
        return rareAchievementsPath;
    }

    private static async Task WriteMissingCharactersAsync(
        string outputPath,
        IReadOnlyList<CharacterToScan> missingCharacters,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = outputPath + ".tmp";
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
        await using (var writer = new StreamWriter(stream, encoding))
        {
            foreach (var character in missingCharacters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteLineAsync($"{character.Name}-{character.DisplayRealm}");
            }
        }

        File.Move(tempPath, outputPath, overwrite: true);
    }

    private static async Task<string?> RefreshLastUpdatedAsync(
        string lastUpdatedPath,
        bool hasExportUpdates,
        CancellationToken cancellationToken)
    {
        if (!hasExportUpdates)
        {
            return null;
        }

        var directory = Path.GetDirectoryName(lastUpdatedPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = lastUpdatedPath + ".tmp";
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var content = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4 * 1024, useAsync: true))
        await using (var writer = new StreamWriter(stream, encoding))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteAsync(content);
        }

        File.Move(tempPath, lastUpdatedPath, overwrite: true);
        return lastUpdatedPath;
    }

    private static int FindColumnIndex(IReadOnlyList<string> header, string columnName)
    {
        for (var i = 0; i < header.Count; i++)
        {
            if (string.Equals(header[i], columnName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var character = line[i];

            if (character == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (character == ',' && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        values.Add(current.ToString());
        return values;
    }

    private static int ReadIntValue(IReadOnlyList<string> values, int index)
    {
        if (index < 0 || index >= values.Count)
        {
            return 0;
        }

        return int.TryParse(values[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue)
            ? parsedValue
            : 0;
    }

    private static string ReadStringValue(IReadOnlyList<string> values, int index)
    {
        if (index < 0 || index >= values.Count)
        {
            return string.Empty;
        }

        return values[index].Trim();
    }

    private static string BuildCsvLine(Player player)
    {
        static string Quote(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";

        return string.Join(",",
            Quote(player.Name),
            player.Race.ToString(CultureInfo.InvariantCulture),
            player.Gender.ToString(CultureInfo.InvariantCulture),
            player.Class.ToString(CultureInfo.InvariantCulture),
            Quote(player.Realm),
            Quote(player.Guild),
            player.AchievementPoints.ToString(CultureInfo.InvariantCulture),
            player.HonorableKills.ToString(CultureInfo.InvariantCulture),
            Quote(player.Faction));
    }

    private static bool NeedsLeadingNewLine(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length == 0)
        {
            return false;
        }

        stream.Seek(-1, SeekOrigin.End);
        var lastByte = stream.ReadByte();
        return lastByte is not '\n';
    }

    private sealed class ScanRequirements
    {
        public bool RequiresPlayerBackfill { get; set; }

        public bool RequiresRareAchievementBackfill { get; set; }
    }

    private readonly record struct CharacterKey(string Name, string Realm);

    private readonly record struct CharacterToScan(string Name, string ApiRealm, string DisplayRealm);

    private readonly record struct BackfillTarget(
        CharacterToScan Character,
        bool RequiresPlayerBackfill,
        bool RequiresRareAchievementBackfill);

    private readonly record struct CharacterBackfillResult(
        Player? Player,
        bool PlayerSucceeded,
        IReadOnlyList<CharacterRareAchievement> RareAchievements,
        bool RareAchievementsSucceeded)
    {
        public bool IsFullySuccessful => PlayerSucceeded && RareAchievementsSucceeded;
    }

    private readonly record struct PlayerFetchResult(Player? Player, bool Succeeded)
    {
        public static PlayerFetchResult Success(Player player) => new(player, true);

        public static PlayerFetchResult Failure() => new(null, false);

        public static PlayerFetchResult Skipped() => new(null, true);
    }

    private readonly record struct RareAchievementFetchResult(
        IReadOnlyList<CharacterRareAchievement> Achievements,
        bool Succeeded)
    {
        public static RareAchievementFetchResult Success(IReadOnlyList<CharacterRareAchievement> achievements) =>
            new(achievements, true);

        public static RareAchievementFetchResult Failure() =>
            new(Array.Empty<CharacterRareAchievement>(), false);

        public static RareAchievementFetchResult Skipped() =>
            new(Array.Empty<CharacterRareAchievement>(), true);
    }

    private sealed class CharacterKeyComparer : IEqualityComparer<CharacterKey>
    {
        public bool Equals(CharacterKey x, CharacterKey y) =>
            string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Realm, y.Realm, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(CharacterKey obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Realm));
        }
    }

    private sealed class CharacterToScanComparer : IEqualityComparer<CharacterToScan>
    {
        public bool Equals(CharacterToScan x, CharacterToScan y) =>
            string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.DisplayRealm, y.DisplayRealm, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(CharacterToScan obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.DisplayRealm));
        }
    }

    private sealed class RareAchievementEntryComparer : IEqualityComparer<CharacterRareAchievementEntry>
    {
        public bool Equals(CharacterRareAchievementEntry? x, CharacterRareAchievementEntry? y)
        {
            if (x is null || y is null)
            {
                return x is null && y is null;
            }

            return string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(x.Realm, y.Realm, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(CharacterRareAchievementEntry obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Realm));
        }
    }
}
