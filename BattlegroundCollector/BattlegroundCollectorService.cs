using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tauri.Core.Infrastructure;

namespace BattlegroundCollector;

public sealed class BattlegroundCollectorService(
    string projectRoot,
    string solutionRoot,
    string frontendSrcDirectory,
    ITauriApiClient apiClient
)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly HashSet<string> ExcludedBattlegroundNames = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "Nagrand Arena",
        "Ruins of Lordaeron",
        "The Tiger's Peak",
        "Tol'Viron Arena",
        "Dalaran Sewers",
    };

    private static readonly (string Token, string FileName)[] GuildFileByRealm =
    [
        ("Evermoon", "evermoon-guilds.txt"),
        ("Tauri", "tauri-guilds.txt"),
        ("Warriors of Darkness", "wod-guilds.txt"),
        ("WoD", "wod-guilds.txt"),
    ];

    private readonly string _projectRoot = Path.GetFullPath(projectRoot);
    private readonly string _guildsDirectory = Path.Combine(
        solutionRoot,
        "AchievementLadder",
        "Data",
        "Guilds"
    );
    private readonly string _frontendSrcDirectory = Path.GetFullPath(frontendSrcDirectory);
    private readonly ITauriApiClient _apiClient = apiClient;

    public async Task<BattlegroundCollectionResult> ExecuteAsync(
        BattlegroundCollectorOptions options,
        CancellationToken cancellationToken
    )
    {
        var outputPath = ResolveOutputPath(options.OutputPath);
        var statePath = ResolveStatePath(options.StatePath);
        var existingRecords = await LoadExistingRecordsAsync(outputPath, cancellationToken);
        var existingState = await LoadStateAsync(statePath, cancellationToken);
        var startMatchId = ResolveStartMatchId(options, existingState, statePath);
        var currentMatchId = startMatchId;
        var newRecords = new List<BattlegroundRecord>();
        var newMembers = new List<MatchMember>();
        var knownMatchIds = existingRecords
            .Where(record => record.Id > 0)
            .Select(record => record.Id)
            .ToHashSet();
        var knownRecordKeys = existingRecords
            .Select(GetRecordKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Console.WriteLine(
            $"Scanning battlegrounds on {options.DisplayRealm} from match id {startMatchId}..."
        );

        string stopReason;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fetchResult = await FetchBattlegroundAsync(
                _apiClient,
                options.ApiRealm,
                currentMatchId,
                cancellationToken
            );

            if (fetchResult.Record is null)
            {
                stopReason = fetchResult.StopReason ?? "No battleground response.";
                Console.WriteLine($"Stopping at match id {currentMatchId}: {stopReason}");
                break;
            }

            if (IsExcludedBattlegroundName(fetchResult.Record.Name))
            {
                Console.WriteLine(
                    $"  - {fetchResult.Record.Id}: skipping excluded map '{fetchResult.Record.Name}'."
                );
                currentMatchId++;
                continue;
            }

            var recordKey = GetRecordKey(fetchResult.Record);
            var isKnownId = fetchResult.Record.Id > 0 && !knownMatchIds.Add(fetchResult.Record.Id);
            var isKnownRecord = !knownRecordKeys.Add(recordKey);
            if (!isKnownId && !isKnownRecord)
            {
                newRecords.Add(fetchResult.Record);
                newMembers.AddRange(fetchResult.Members);
                Console.WriteLine(
                    $"  + {fetchResult.Record.Id}: {fetchResult.Record.Name} at {fetchResult.Record.StartTime}"
                );
            }
            else
            {
                Console.WriteLine($"  = {fetchResult.Record.Id}: already exists in output JSON.");
            }

            currentMatchId++;
        }

        var mergedRecords = MergeRecords(newRecords, existingRecords);
        await WriteJsonAsync(outputPath, mergedRecords, cancellationToken);
        var newGuildCount = CollectUnknownGuilds(newMembers);

        var savedState = new BattlegroundCollectorState(
            currentMatchId,
            options.ApiRealm,
            options.DisplayRealm,
            DateTimeOffset.UtcNow,
            stopReason,
            newRecords.Count,
            newRecords.Count == 0 ? null : newRecords.Max(record => record.Id)
        );

        await WriteJsonAsync(statePath, savedState, cancellationToken);

        return new BattlegroundCollectionResult(
            startMatchId,
            currentMatchId,
            newRecords.Count,
            mergedRecords.Count,
            newGuildCount,
            outputPath,
            statePath,
            stopReason
        );
    }

    private static async Task<BattlegroundFetchResult> FetchBattlegroundAsync(
        ITauriApiClient apiClient,
        string apiRealm,
        int matchId,
        CancellationToken cancellationToken
    )
    {
        var result = await apiClient.FetchResponseElementAsync(
            "pvp-match",
            new
            {
                r = apiRealm,
                matchid = matchId.ToString(CultureInfo.InvariantCulture),
            },
            $"battleground match {matchId} on {apiRealm}",
            cancellationToken
        );

        if (!result.Succeeded || result.ResponseElement is not { } responseElement)
        {
            return BattlegroundFetchResult.Stop(
                $"API request failed: {result.FailureMessage ?? "No response payload."}"
            );
        }

        if (
            responseElement.ValueKind
                is JsonValueKind.Null
                    or JsonValueKind.Undefined
                    or JsonValueKind.False
        )
        {
            return BattlegroundFetchResult.Stop("Missing response payload.");
        }

        if (responseElement.ValueKind != JsonValueKind.Object)
        {
            return BattlegroundFetchResult.Stop(
                $"Response payload was {responseElement.ValueKind}, not an object."
            );
        }

        var apiMatchId = ReadInt(responseElement, "matchid");
        if (apiMatchId <= 0)
        {
            apiMatchId = matchId;
        }

        var startTimeUnix = ReadLong(responseElement, "starttime");
        var mapName = ReadString(responseElement, "mapname");
        if (string.IsNullOrWhiteSpace(mapName) || startTimeUnix <= 0)
        {
            return BattlegroundFetchResult.Stop(
                "Response did not contain complete battleground details."
            );
        }

        var durationMilliseconds = ReadLong(responseElement, "length");
        var record = new BattlegroundRecord(
            apiMatchId,
            mapName,
            FormatUnixTimestamp(startTimeUnix),
            FormatDuration(durationMilliseconds)
        );
        var members = ReadMembers(responseElement);

        return BattlegroundFetchResult.Found(record, members);
    }

    private int ResolveStartMatchId(
        BattlegroundCollectorOptions options,
        BattlegroundCollectorState? state,
        string statePath
    )
    {
        if (options.StartMatchId is { } explicitStartMatchId)
        {
            return explicitStartMatchId;
        }

        if (state is null || state.NextMatchId <= 0)
        {
            throw new InvalidOperationException(
                $"No saved battleground collector state exists yet. Run once with a start match id, for example: dotnet run --project BattlegroundCollector -- 95874. State path: {statePath}"
            );
        }

        if (!string.Equals(state.ApiRealm, options.ApiRealm, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The saved state is for {state.DisplayRealm}, but this run targets {options.DisplayRealm}. Pass a start match id or use a different --state path."
            );
        }

        return state.NextMatchId;
    }

    private string ResolveOutputPath(string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(outputPath, _projectRoot);
        }

        return Path.Combine(_frontendSrcDirectory, "battlegrounds.json");
    }

    private string ResolveStatePath(string? statePath)
    {
        if (!string.IsNullOrWhiteSpace(statePath))
        {
            return Path.GetFullPath(statePath, _projectRoot);
        }

        return Path.Combine(_frontendSrcDirectory, "battleground-collector-state.json");
    }

    private static async Task<List<BattlegroundRecord>> LoadExistingRecordsAsync(
        string outputPath,
        CancellationToken cancellationToken
    )
    {
        if (!File.Exists(outputPath))
        {
            return [];
        }

        await using var stream = new FileStream(
            outputPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            useAsync: true
        );

        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken
        );
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var records = new List<BattlegroundRecord>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = ReadString(element, "name", ReadString(element, "bgName"));
            if (string.IsNullOrWhiteSpace(name) || IsExcludedBattlegroundName(name))
            {
                continue;
            }

            var startTime = ReadString(element, "startTime", ReadString(element, "bgStartTime"));
            if (string.IsNullOrWhiteSpace(startTime))
            {
                startTime = FormatUnixTimestamp(ReadLong(element, "bgStartTimeUnix"));
            }

            var duration = ReadDuration(element);

            records.Add(
                new BattlegroundRecord(
                    ReadInt(element, "id", ReadInt(element, "bgId")),
                    name,
                    startTime,
                    duration
                )
            );
        }

        return records;
    }

    private static async Task<BattlegroundCollectorState?> LoadStateAsync(
        string statePath,
        CancellationToken cancellationToken
    )
    {
        if (!File.Exists(statePath))
        {
            return null;
        }

        await using var stream = new FileStream(
            statePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            useAsync: true
        );

        return await JsonSerializer.DeserializeAsync<BattlegroundCollectorState>(
            stream,
            JsonOptions,
            cancellationToken
        );
    }

    private static List<BattlegroundRecord> MergeRecords(
        IReadOnlyList<BattlegroundRecord> newRecords,
        IReadOnlyList<BattlegroundRecord> existingRecords
    )
    {
        var mergedRecords = new List<BattlegroundRecord>(newRecords.Count + existingRecords.Count);
        var seenMatchIds = new HashSet<int>();
        var seenRecordKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in newRecords.OrderByDescending(record => record.Id))
        {
            if (TryAddRecord(record, seenMatchIds, seenRecordKeys))
            {
                mergedRecords.Add(record);
            }
        }

        foreach (var record in existingRecords)
        {
            if (IsExcludedBattlegroundName(record.Name))
            {
                continue;
            }

            if (TryAddRecord(record, seenMatchIds, seenRecordKeys))
            {
                mergedRecords.Add(record);
            }
        }

        return mergedRecords;
    }

    private int CollectUnknownGuilds(IReadOnlyList<MatchMember> members)
    {
        Console.WriteLine();
        Console.WriteLine("=== Guild collection ===");

        if (members.Count == 0)
        {
            Console.WriteLine("No new battleground members to check for guilds.");
            return 0;
        }

        Directory.CreateDirectory(_guildsDirectory);

        var knownGuildsByFile = new Dictionary<string, HashSet<string>>(
            StringComparer.OrdinalIgnoreCase
        );
        var newGuildsByFile = new Dictionary<string, List<string>>(
            StringComparer.OrdinalIgnoreCase
        );
        var addedCount = 0;
        var skippedUnknownRealm = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var distinctMembers = members
            .DistinctBy(member => $"{member.CharName}|{member.RealmName}".ToLowerInvariant())
            .OrderBy(member => member.CharName, StringComparer.OrdinalIgnoreCase);

        foreach (var member in distinctMembers)
        {
            if (string.IsNullOrWhiteSpace(member.GuildName))
            {
                continue;
            }

            var fileName = ResolveGuildFileName(member.RealmName);
            if (fileName is null)
            {
                if (skippedUnknownRealm.Add(member.RealmName))
                {
                    Console.WriteLine(
                        $"  No guild file for realm '{member.RealmName}' - skipping its guilds."
                    );
                }

                continue;
            }

            var filePath = Path.Combine(_guildsDirectory, fileName);
            if (!knownGuildsByFile.TryGetValue(fileName, out var knownGuilds))
            {
                knownGuilds = LoadGuildSet(filePath);
                knownGuildsByFile[fileName] = knownGuilds;
            }

            if (!knownGuilds.Add(member.GuildName))
            {
                continue;
            }

            if (!newGuildsByFile.TryGetValue(fileName, out var newGuilds))
            {
                newGuilds = [];
                newGuildsByFile[fileName] = newGuilds;
            }

            newGuilds.Add(member.GuildName);
            addedCount++;
            Console.WriteLine($"  + Added '{member.GuildName}' to {fileName}");
        }

        foreach (var (fileName, newGuilds) in newGuildsByFile)
        {
            AppendGuildNames(Path.Combine(_guildsDirectory, fileName), newGuilds);
        }

        Console.WriteLine(
            addedCount == 0
                ? "No new guilds - all guilds already known."
                : $"Added {addedCount} new guild name(s)."
        );

        return addedCount;
    }

    private static async Task WriteJsonAsync<T>(
        string outputPath,
        T value,
        CancellationToken cancellationToken
    )
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = outputPath + ".tmp";
        var json = JsonSerializer.Serialize(value, JsonOptions);
        await File.WriteAllTextAsync(tempPath, json, Utf8NoBom, cancellationToken);
        File.Move(tempPath, outputPath, overwrite: true);
    }

    private static int ReadInt(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var intValue))
        {
            return intValue;
        }

        return
            property.ValueKind == JsonValueKind.String
            && int.TryParse(
                property.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out intValue
            )
            ? intValue
            : 0;
    }

    private static int ReadInt(JsonElement parent, string propertyName, int fallback)
    {
        var value = ReadInt(parent, propertyName);
        return value == 0 ? fallback : value;
    }

    private static long ReadLong(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var longValue))
        {
            return longValue;
        }

        return
            property.ValueKind == JsonValueKind.String
            && long.TryParse(
                property.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out longValue
            )
            ? longValue
            : 0;
    }

    private static long ReadLong(JsonElement parent, string propertyName, long fallback)
    {
        var value = ReadLong(parent, propertyName);
        return value == 0 ? fallback : value;
    }

    private static string ReadString(JsonElement parent, string propertyName, string fallback = "")
    {
        if (
            !parent.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
        )
        {
            return fallback;
        }

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static List<MatchMember> ReadMembers(JsonElement responseElement)
    {
        if (
            !responseElement.TryGetProperty("members", out var membersElement)
            || membersElement.ValueKind != JsonValueKind.Array
        )
        {
            return [];
        }

        var members = new List<MatchMember>();
        foreach (var memberElement in membersElement.EnumerateArray())
        {
            if (
                memberElement.ValueKind != JsonValueKind.Object
                || !memberElement.TryGetProperty("character-minimal-data", out var minimal)
                || minimal.ValueKind != JsonValueKind.Object
            )
            {
                continue;
            }

            var charName = ReadString(minimal, "charname");
            if (string.IsNullOrWhiteSpace(charName))
            {
                continue;
            }

            members.Add(
                new MatchMember(
                    charName,
                    ReadString(minimal, "guildname"),
                    ReadString(memberElement, "realmName")
                )
            );
        }

        return members;
    }

    private static string? ResolveGuildFileName(string realmName)
    {
        if (string.IsNullOrWhiteSpace(realmName))
        {
            return null;
        }

        foreach (var (token, fileName) in GuildFileByRealm)
        {
            if (realmName.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return fileName;
            }
        }

        return null;
    }

    private static HashSet<string> LoadGuildSet(string filePath)
    {
        var guilds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(filePath))
        {
            return guilds;
        }

        foreach (var line in File.ReadLines(filePath))
        {
            var trimmed = line.Trim().TrimStart('\uFEFF');
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                guilds.Add(trimmed);
            }
        }

        return guilds;
    }

    private static void AppendGuildNames(string filePath, IReadOnlyList<string> guildNames)
    {
        var builder = new StringBuilder();

        if (File.Exists(filePath) && !EndsWithNewline(filePath))
        {
            builder.Append(Environment.NewLine);
        }

        foreach (var guildName in guildNames)
        {
            builder.Append(guildName).Append(Environment.NewLine);
        }

        File.AppendAllText(filePath, builder.ToString(), Utf8NoBom);
    }

    private static bool EndsWithNewline(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        if (stream.Length == 0)
        {
            return true;
        }

        stream.Seek(-1, SeekOrigin.End);
        var lastByte = stream.ReadByte();
        return lastByte is '\n' or '\r';
    }

    private static bool TryAddRecord(
        BattlegroundRecord record,
        HashSet<int> seenMatchIds,
        HashSet<string> seenRecordKeys
    )
    {
        var recordKey = GetRecordKey(record);
        if (record.Id > 0)
        {
            if (!seenMatchIds.Add(record.Id))
            {
                return false;
            }

            seenRecordKeys.Add(recordKey);
            return true;
        }

        return seenRecordKeys.Add(recordKey);
    }

    private static string GetRecordKey(BattlegroundRecord record) =>
        $"{record.Name}|{record.StartTime}|{record.Duration}";

    private static bool IsExcludedBattlegroundName(string name) =>
        ExcludedBattlegroundNames.Contains(name.Trim());

    private static string ReadDuration(JsonElement element)
    {
        var duration = ReadString(element, "duration", ReadString(element, "bgDurationFormatted"));
        if (
            long.TryParse(
                duration,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var durationMilliseconds
            )
        )
        {
            return FormatDuration(durationMilliseconds);
        }

        if (!string.IsNullOrWhiteSpace(duration))
        {
            return duration;
        }

        return FormatDuration(ReadLong(element, "bgDuration"));
    }

    private static string FormatUnixTimestamp(long unixTimestamp)
    {
        if (unixTimestamp <= 0)
        {
            return string.Empty;
        }

        return DateTimeOffset
            .FromUnixTimeSeconds(unixTimestamp)
            .ToLocalTime()
            .ToString("yyyy.MM.dd HH.mm", CultureInfo.InvariantCulture);
    }

    private static string FormatDuration(long durationMilliseconds)
    {
        if (durationMilliseconds <= 0)
        {
            return "00:00:00";
        }

        var duration = TimeSpan.FromMilliseconds(durationMilliseconds);
        return $"{(long)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
    }

}

public sealed record BattlegroundRecord(
    [property: JsonIgnore] int Id,
    string Name,
    string StartTime,
    string Duration
);

public sealed record BattlegroundCollectorState(
    int NextMatchId,
    string ApiRealm,
    string DisplayRealm,
    DateTimeOffset LastScanUtc,
    string LastStopReason,
    int LastAddedCount,
    int? LastAddedMatchId
);

public sealed record BattlegroundCollectionResult(
    int StartMatchId,
    int NextMatchId,
    int NewBattlegroundCount,
    int TotalBattlegroundCount,
    int NewGuildCount,
    string OutputPath,
    string StatePath,
    string StopReason
);

internal sealed record BattlegroundFetchResult(
    BattlegroundRecord? Record,
    IReadOnlyList<MatchMember> Members,
    string? StopReason
)
{
    public static BattlegroundFetchResult Found(
        BattlegroundRecord record,
        IReadOnlyList<MatchMember> members
    ) => new(record, members, null);

    public static BattlegroundFetchResult Stop(string reason) => new(null, [], reason);
}

internal sealed record MatchMember(string CharName, string GuildName, string RealmName);
