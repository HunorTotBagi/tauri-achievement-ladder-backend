using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text;
using AchievementLadder.Configuration;
using AchievementLadder.Helpers;
using AchievementLadder.Models;

namespace MissingPlayerFinder;

public sealed class MissingPlayerFinderService(
    string solutionRoot,
    string achievementLadderProjectRoot,
    TauriApiOptions apiOptions)
{
    private static readonly CharacterKeyComparer CharacterComparer = new();
    private const int ProgressInterval = 100;
    private const int MaxDegreeOfParallelism = 20;

    private readonly string _solutionRoot = Path.GetFullPath(solutionRoot);
    private readonly string _achievementLadderProjectRoot = Path.GetFullPath(achievementLadderProjectRoot);
    private readonly TauriApiOptions _apiOptions = apiOptions;

    public async Task<MissingPlayerFinderResult> GenerateAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_achievementLadderProjectRoot))
        {
            throw new DirectoryNotFoundException(
                $"Could not find AchievementLadder project folder: {_achievementLadderProjectRoot}");
        }

        var playersCsvPath = Path.Combine(_solutionRoot, "Players.csv");
        if (!File.Exists(playersCsvPath))
        {
            throw new FileNotFoundException($"Could not find Players.csv at {playersCsvPath}", playersCsvPath);
        }

        var sourceCharacters = LoadSourceCharacters(cancellationToken);
        var csvCharacters = LoadCsvCharacters(playersCsvPath, cancellationToken);

        var missingCharacters = sourceCharacters
            .Where(character => !csvCharacters.Contains(new CharacterKey(character.Name, character.DisplayRealm)))
            .OrderBy(character => character.DisplayRealm, StringComparer.OrdinalIgnoreCase)
            .ThenBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Console.WriteLine($"Missing characters at start: {missingCharacters.Count}");

        var fetchedPlayers = new ConcurrentBag<Player>();
        var unresolvedCharacters = new ConcurrentBag<CharacterToScan>();
        var apiUrl = BuildApiUrl(_apiOptions.BaseUrl, _apiOptions.ApiKey);

        using var client = new HttpClient();
        var processedCount = 0;

        await Parallel.ForEachAsync(
            missingCharacters,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxDegreeOfParallelism,
                CancellationToken = cancellationToken
            },
            async (character, ct) =>
            {
                var player = await FetchPlayerAsync(
                    client,
                    apiUrl,
                    _apiOptions.Secret,
                    character,
                    ct);

                if (player is null)
                {
                    unresolvedCharacters.Add(character);
                }
                else
                {
                    fetchedPlayers.Add(player);
                }

                var processed = Interlocked.Increment(ref processedCount);
                if (processed % ProgressInterval == 0 || processed == missingCharacters.Count)
                {
                    Console.Write($"\rBackfill progress: {processed}/{missingCharacters.Count}");
                }
            });

        if (missingCharacters.Count > 0)
        {
            Console.WriteLine();
        }

        var orderedPlayers = fetchedPlayers
            .OrderByDescending(player => player.AchievementPoints)
            .ThenByDescending(player => player.HonorableKills)
            .ThenBy(player => player.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(player => player.Realm, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await AppendPlayersAsync(playersCsvPath, orderedPlayers, cancellationToken);

        var remainingCharacters = unresolvedCharacters
            .OrderBy(character => character.DisplayRealm, StringComparer.OrdinalIgnoreCase)
            .ThenBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var outputPath = Path.Combine(_solutionRoot, "MissingPlayersToScan.txt");
        await WriteMissingCharactersAsync(outputPath, remainingCharacters, cancellationToken);

        return new MissingPlayerFinderResult(
            sourceCharacters.Count,
            csvCharacters.Count,
            missingCharacters.Count,
            orderedPlayers.Count,
            remainingCharacters.Count,
            playersCsvPath,
            outputPath);
    }

    private HashSet<CharacterToScan> LoadSourceCharacters(CancellationToken cancellationToken)
    {
        var allCharacters = new List<(string Name, string ApiRealm, string DisplayRealm)>();

        CharacterHelpers.LoadGuildCharacters(_achievementLadderProjectRoot, "GuildCharacters.txt", allCharacters);

        CharacterHelpers.LoadCharacters(_achievementLadderProjectRoot, "evermoon-achi.txt", "[EN] Evermoon", "Evermoon", allCharacters);
        CharacterHelpers.LoadCharacters(_achievementLadderProjectRoot, "evermoon-hk.txt", "[EN] Evermoon", "Evermoon", allCharacters);
        CharacterHelpers.LoadCharacters(_achievementLadderProjectRoot, "evermoon-playTime.txt", "[EN] Evermoon", "Evermoon", allCharacters);

        CharacterHelpers.LoadCharacters(_achievementLadderProjectRoot, "tauri-achi.txt", "[HU] Tauri WoW Server", "Tauri", allCharacters);
        CharacterHelpers.LoadCharacters(_achievementLadderProjectRoot, "tauri-hk.txt", "[HU] Tauri WoW Server", "Tauri", allCharacters);
        CharacterHelpers.LoadCharacters(_achievementLadderProjectRoot, "tauri-playTime.txt", "[HU] Tauri WoW Server", "Tauri", allCharacters);

        CharacterHelpers.LoadCharacters(_achievementLadderProjectRoot, "wod-achi.txt", "[HU] Warriors of Darkness", "WoD", allCharacters);
        CharacterHelpers.LoadCharacters(_achievementLadderProjectRoot, "wod-hk.txt", "[HU] Warriors of Darkness", "WoD", allCharacters);
        CharacterHelpers.LoadCharacters(_achievementLadderProjectRoot, "wod-playTime.txt", "[HU] Warriors of Darkness", "WoD", allCharacters);

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

    private HashSet<CharacterKey> LoadCsvCharacters(string playersCsvPath, CancellationToken cancellationToken)
    {
        using var lines = File.ReadLines(playersCsvPath).GetEnumerator();
        if (!lines.MoveNext())
        {
            return new HashSet<CharacterKey>(CharacterComparer);
        }

        var header = ParseCsvLine(lines.Current);
        var nameIndex = FindColumnIndex(header, "Name");
        var realmIndex = FindColumnIndex(header, "Realm");

        if (nameIndex < 0 || realmIndex < 0)
        {
            throw new InvalidDataException("Players.csv must contain Name and Realm columns.");
        }

        var result = new HashSet<CharacterKey>(CharacterComparer);

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

            result.Add(new CharacterKey(name, realm));
        }

        return result;
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

    private static async Task<Player?> FetchPlayerAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        CharacterToScan character,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            secret,
            url = "character-sheet",
            @params = new
            {
                r = character.ApiRealm,
                n = character.Name
            }
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        try
        {
            using var response = await client.PostAsync(apiUrl, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var root = doc.RootElement;
            if (!root.TryGetProperty("response", out var responseElement))
            {
                return null;
            }

            int race = responseElement.TryGetProperty("race", out var value) ? value.GetInt32() : 0;
            int gender = responseElement.TryGetProperty("gender", out value) ? value.GetInt32() : 0;
            int @class = responseElement.TryGetProperty("class", out value) ? value.GetInt32() : 0;
            int achievementPoints = responseElement.TryGetProperty("pts", out value) ? value.GetInt32() : 0;
            int honorableKills = responseElement.TryGetProperty("playerHonorKills", out value) ? value.GetInt32() : 0;
            string faction = responseElement.TryGetProperty("faction_string_class", out value)
                ? (value.GetString() ?? string.Empty)
                : string.Empty;
            string guild = responseElement.TryGetProperty("guildName", out value)
                ? (value.GetString() ?? string.Empty)
                : string.Empty;

            return new Player
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
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
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

    private static string BuildApiUrl(string baseUrl, string apiKey)
    {
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseUrl}{separator}apikey={Uri.EscapeDataString(apiKey)}";
    }

    private readonly record struct CharacterKey(string Name, string Realm);

    private readonly record struct CharacterToScan(string Name, string ApiRealm, string DisplayRealm);

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
}
