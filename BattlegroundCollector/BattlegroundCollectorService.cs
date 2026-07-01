using System.Globalization;
using System.Text;
using System.Text.Json;
using AchievementLadder.Configuration;

namespace BattlegroundCollector;

public sealed class BattlegroundCollectorService(
    string projectRoot,
    string frontendSrcDirectory,
    TauriApiOptions apiOptions)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _projectRoot = Path.GetFullPath(projectRoot);
    private readonly string _frontendSrcDirectory = Path.GetFullPath(frontendSrcDirectory);
    private readonly TauriApiOptions _apiOptions = apiOptions;

    public async Task<BattlegroundCollectionResult> ExecuteAsync(
        BattlegroundCollectorOptions options,
        CancellationToken cancellationToken)
    {
        var outputPath = ResolveOutputPath(options.OutputPath);
        var statePath = ResolveStatePath(options.StatePath);
        var existingRecords = await LoadExistingRecordsAsync(outputPath, cancellationToken);
        var existingState = await LoadStateAsync(statePath, cancellationToken);
        var startMatchId = ResolveStartMatchId(options, existingState, statePath);
        var apiUrl = BuildApiUrl(_apiOptions.BaseUrl, _apiOptions.ApiKey);
        var currentMatchId = startMatchId;
        var newRecords = new List<BattlegroundRecord>();
        var knownBgIds = existingRecords
            .Select(record => record.BgId)
            .ToHashSet();

        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(_apiOptions.RequestTimeoutSeconds)
        };

        Console.WriteLine($"Scanning battlegrounds on {options.DisplayRealm} from match id {startMatchId}...");

        string stopReason;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fetchResult = await FetchBattlegroundAsync(
                client,
                apiUrl,
                _apiOptions.Secret,
                options.ApiRealm,
                currentMatchId,
                cancellationToken);

            if (fetchResult.Record is null)
            {
                stopReason = fetchResult.StopReason ?? "No battleground response.";
                Console.WriteLine($"Stopping at match id {currentMatchId}: {stopReason}");
                break;
            }

            if (knownBgIds.Add(fetchResult.Record.BgId))
            {
                newRecords.Add(fetchResult.Record);
                Console.WriteLine(
                    $"  + {fetchResult.Record.BgId}: {fetchResult.Record.BgName} at {fetchResult.Record.BgStartTime}");
            }
            else
            {
                Console.WriteLine($"  = {fetchResult.Record.BgId}: already exists in output JSON.");
            }

            currentMatchId++;
        }

        var mergedRecords = MergeRecords(newRecords, existingRecords);
        if (newRecords.Count > 0 || !File.Exists(outputPath))
        {
            await WriteJsonAsync(outputPath, mergedRecords, cancellationToken);
        }

        var savedState = new BattlegroundCollectorState(
            currentMatchId,
            options.ApiRealm,
            options.DisplayRealm,
            DateTimeOffset.UtcNow,
            stopReason,
            newRecords.Count,
            newRecords.Count == 0 ? null : newRecords.Max(record => record.BgId));

        await WriteJsonAsync(statePath, savedState, cancellationToken);

        return new BattlegroundCollectionResult(
            startMatchId,
            currentMatchId,
            newRecords.Count,
            mergedRecords.Count,
            outputPath,
            statePath,
            stopReason);
    }

    private static async Task<BattlegroundFetchResult> FetchBattlegroundAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        string apiRealm,
        int matchId,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            secret,
            url = "pvp-match",
            @params = new
            {
                r = apiRealm,
                matchid = matchId.ToString(CultureInfo.InvariantCulture)
            }
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync(apiUrl, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return BattlegroundFetchResult.Stop(
                $"API returned {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        if (root.TryGetProperty("success", out var successElement) && IsExplicitFalse(successElement))
        {
            return BattlegroundFetchResult.Stop("API returned success=false.");
        }

        if (!root.TryGetProperty("response", out var responseElement) ||
            responseElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined or JsonValueKind.False)
        {
            return BattlegroundFetchResult.Stop("Missing response payload.");
        }

        if (responseElement.ValueKind != JsonValueKind.Object)
        {
            return BattlegroundFetchResult.Stop($"Response payload was {responseElement.ValueKind}, not an object.");
        }

        var apiMatchId = ReadInt(responseElement, "matchid");
        if (apiMatchId <= 0)
        {
            apiMatchId = matchId;
        }

        var startTimeUnix = ReadLong(responseElement, "starttime");
        var durationMilliseconds = ReadLong(responseElement, "length");
        var record = new BattlegroundRecord(
            apiMatchId,
            ReadString(responseElement, "mapname", fallback: "Unknown Battleground"),
            FormatUnixTimestamp(startTimeUnix),
            startTimeUnix,
            FormatUnixDate(startTimeUnix),
            durationMilliseconds,
            FormatDuration(durationMilliseconds));

        return BattlegroundFetchResult.Found(record);
    }

    private static bool IsExplicitFalse(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.False)
        {
            return true;
        }

        return element.ValueKind == JsonValueKind.String &&
               bool.TryParse(element.GetString(), out var value) &&
               !value;
    }

    private int ResolveStartMatchId(
        BattlegroundCollectorOptions options,
        BattlegroundCollectorState? state,
        string statePath)
    {
        if (options.StartMatchId is { } explicitStartMatchId)
        {
            return explicitStartMatchId;
        }

        if (state is null || state.NextMatchId <= 0)
        {
            throw new InvalidOperationException(
                $"No saved battleground collector state exists yet. Run once with a start match id, for example: dotnet run --project BattlegroundCollector -- 95874. State path: {statePath}");
        }

        if (!string.Equals(state.ApiRealm, options.ApiRealm, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The saved state is for {state.DisplayRealm}, but this run targets {options.DisplayRealm}. Pass a start match id or use a different --state path.");
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
        CancellationToken cancellationToken)
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
            useAsync: true);

        return await JsonSerializer.DeserializeAsync<List<BattlegroundRecord>>(
            stream,
            JsonOptions,
            cancellationToken) ?? [];
    }

    private static async Task<BattlegroundCollectorState?> LoadStateAsync(
        string statePath,
        CancellationToken cancellationToken)
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
            useAsync: true);

        return await JsonSerializer.DeserializeAsync<BattlegroundCollectorState>(
            stream,
            JsonOptions,
            cancellationToken);
    }

    private static List<BattlegroundRecord> MergeRecords(
        IReadOnlyList<BattlegroundRecord> newRecords,
        IReadOnlyList<BattlegroundRecord> existingRecords)
    {
        var mergedRecords = new List<BattlegroundRecord>(newRecords.Count + existingRecords.Count);
        var seenBgIds = new HashSet<int>();

        foreach (var record in newRecords.OrderByDescending(record => record.BgId))
        {
            if (seenBgIds.Add(record.BgId))
            {
                mergedRecords.Add(record);
            }
        }

        foreach (var record in existingRecords)
        {
            if (seenBgIds.Add(record.BgId))
            {
                mergedRecords.Add(record);
            }
        }

        return mergedRecords;
    }

    private static async Task WriteJsonAsync<T>(
        string outputPath,
        T value,
        CancellationToken cancellationToken)
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

        return property.ValueKind == JsonValueKind.String &&
               int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue)
            ? intValue
            : 0;
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

        return property.ValueKind == JsonValueKind.String &&
               long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out longValue)
            ? longValue
            : 0;
    }

    private static string ReadString(JsonElement parent, string propertyName, string fallback = "")
    {
        if (!parent.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return fallback;
        }

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
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

    private static string FormatUnixDate(long unixTimestamp)
    {
        if (unixTimestamp <= 0)
        {
            return string.Empty;
        }

        return DateTimeOffset
            .FromUnixTimeSeconds(unixTimestamp)
            .ToLocalTime()
            .ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
    }

    private static string FormatDuration(long durationMilliseconds)
    {
        if (durationMilliseconds <= 0)
        {
            return "00:00:00";
        }

        var duration = TimeSpan.FromMilliseconds(durationMilliseconds);
        return duration.TotalDays >= 1
            ? $"{(int)duration.TotalDays}.{duration:hh\\:mm\\:ss}"
            : duration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }

    private static string BuildApiUrl(string baseUrl, string apiKey)
    {
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseUrl}{separator}apikey={Uri.EscapeDataString(apiKey)}";
    }
}

public sealed record BattlegroundRecord(
    int BgId,
    string BgName,
    string BgStartTime,
    long BgStartTimeUnix,
    string BgStartDate,
    long BgDuration,
    string BgDurationFormatted);

public sealed record BattlegroundCollectorState(
    int NextMatchId,
    string ApiRealm,
    string DisplayRealm,
    DateTimeOffset LastScanUtc,
    string LastStopReason,
    int LastAddedCount,
    int? LastAddedBgId);

public sealed record BattlegroundCollectionResult(
    int StartMatchId,
    int NextMatchId,
    int NewBattlegroundCount,
    int TotalBattlegroundCount,
    string OutputPath,
    string StatePath,
    string StopReason);

internal sealed record BattlegroundFetchResult(
    BattlegroundRecord? Record,
    string? StopReason)
{
    public static BattlegroundFetchResult Found(BattlegroundRecord record) =>
        new(record, null);

    public static BattlegroundFetchResult Stop(string reason) =>
        new(null, reason);
}
