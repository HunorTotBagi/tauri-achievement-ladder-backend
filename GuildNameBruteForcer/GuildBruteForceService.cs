using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using AchievementLadder.Configuration;

namespace GuildNameBruteForcer;

public sealed class GuildBruteForceService : IDisposable
{
    private static readonly RealmInfo[] AllRealms =
    [
        new("evermoon", "[EN] Evermoon", "Evermoon"),
        new("tauri",    "[HU] Tauri WoW Server", "Tauri"),
        new("wod",      "[HU] Warriors of Darkness", "WoD")
    ];

    // Space is included — guild names like "Dark Moon" are valid.
    // Invalid forms (leading/trailing space, consecutive spaces) are filtered before each API call.
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ ";

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _semaphore;
    private readonly string _apiUrl;
    private readonly string _secret;
    private readonly int _maxConcurrent;

    public GuildBruteForceService(TauriApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _secret = options.Secret;
        _maxConcurrent = options.MaxConcurrentRequests;
        _apiUrl = BuildApiUrl(options.BaseUrl, options.ApiKey);
        _semaphore = new SemaphoreSlim(_maxConcurrent, _maxConcurrent);

        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            MaxConnectionsPerServer = _maxConcurrent,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            UseCookies = false
        };

        _httpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public async Task ScanAsync(int nameLength, string? realmFilter, string outputDirectory, CancellationToken cancellationToken)
    {
        var realms = realmFilter is null
            ? AllRealms
            : AllRealms.Where(r => r.Key == realmFilter).ToArray();

        var totalCombinations = CountValidCombinations(nameLength);

        foreach (var realm in realms)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var outputPath = Path.Combine(outputDirectory, $"found-guilds-{realm.Key}-len{nameLength}.txt");
            Console.WriteLine($"Scanning {realm.DisplayRealm} ({totalCombinations:N0} combinations)");
            Console.WriteLine($"  Output: {outputPath}");

            long checkedCount = 0;
            long foundCount = 0;

            var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
            var writerTask = WriteFoundGuildsAsync(outputPath, channel.Reader);

            try
            {
                await Parallel.ForEachAsync(
                    GenerateCombinations(nameLength).Where(IsValidGuildName),
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = _maxConcurrent,
                        CancellationToken = cancellationToken
                    },
                    async (candidate, token) =>
                    {
                        var exists = await CheckGuildExistsAsync(candidate, realm.ApiRealm, token);
                        var current = Interlocked.Increment(ref checkedCount);

                        if (exists)
                        {
                            Interlocked.Increment(ref foundCount);
                            channel.Writer.TryWrite(candidate);
                            Console.WriteLine($"  [FOUND] {candidate} on {realm.DisplayRealm}");
                        }

                        if (current % 1000 == 0 || current == totalCombinations)
                        {
                            Console.WriteLine($"  [{realm.DisplayRealm}] {current:N0}/{totalCombinations:N0} ({current * 100.0 / totalCombinations:F1}%) — found so far: {Interlocked.Read(ref foundCount)}");
                        }
                    });
            }
            finally
            {
                channel.Writer.TryComplete();
                await writerTask;
            }

            Console.WriteLine($"Finished {realm.DisplayRealm}: {foundCount} guild(s) discovered.");
            Console.WriteLine();
        }
    }

    private async Task<bool> CheckGuildExistsAsync(string guildName, string apiRealm, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                secret = _secret,
                url = "guild-info",
                @params = new { r = apiRealm, gn = guildName }
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(10));

            using var response = await _httpClient.PostAsync(_apiUrl, content, timeoutSource.Token);
            if (!response.IsSuccessStatusCode) return false;

            await using var stream = await response.Content.ReadAsStreamAsync(timeoutSource.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: timeoutSource.Token);

            if (!doc.RootElement.TryGetProperty("response", out var responseEl) ||
                responseEl.ValueKind == JsonValueKind.Null)
                return false;

            return responseEl.TryGetProperty("guildList", out var guildList)
                && guildList.ValueKind == JsonValueKind.Object
                && guildList.EnumerateObject().Any();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Request timed out — treat as not found
            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static async Task WriteFoundGuildsAsync(string outputPath, ChannelReader<string> reader)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        await using var stream = new FileStream(outputPath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
        await using var writer = new StreamWriter(stream, encoding);

        await foreach (var guild in reader.ReadAllAsync())
        {
            await writer.WriteLineAsync(guild);
            await writer.FlushAsync();
        }
    }

    private static IEnumerable<string> GenerateCombinations(int length)
    {
        var indices = new int[length];
        var chars = new char[length];

        while (true)
        {
            for (var i = 0; i < length; i++)
                chars[i] = Alphabet[indices[i]];

            yield return new string(chars);

            var pos = length - 1;
            while (pos >= 0)
            {
                indices[pos]++;
                if (indices[pos] < Alphabet.Length) break;
                indices[pos] = 0;
                pos--;
            }

            if (pos < 0) break;
        }
    }

    private static bool IsValidGuildName(string name) =>
        name[0] != ' ' && name[^1] != ' ' && !name.Contains("  ");

    // Counts valid strings of `length` over A-Z + space where:
    // - first and last chars must be letters
    // - no two consecutive spaces
    // Uses DP: fL = ends-in-letter count, fS = ends-in-space count.
    private static long CountValidCombinations(int length)
    {
        if (length == 0) return 0;
        const long k = 26;
        long fL = k, fS = 0;
        for (var i = 1; i < length; i++)
        {
            (fL, fS) = ((fL + fS) * k, fL);
        }
        return fL; // names ending in space are invalid, so only fL counts
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _semaphore.Dispose();
    }

    private static string BuildApiUrl(string baseUrl, string apiKey)
    {
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseUrl}{separator}apikey={Uri.EscapeDataString(apiKey)}";
    }

    private sealed record RealmInfo(string Key, string ApiRealm, string DisplayRealm);
}
