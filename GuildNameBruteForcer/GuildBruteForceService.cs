using System.Text;
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

    private readonly GuildLookupClient _apiClient;
    private readonly int _maxConcurrent;

    public GuildBruteForceService(TauriApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _maxConcurrent = options.MaxConcurrentRequests;
        _apiClient = new GuildLookupClient(options);
    }

    public async Task ScanAsync(
        int nameLength,
        string? realmFilter,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var realms = ResolveRealms(realmFilter);
        var totalCombinations = CountValidCombinations(nameLength);

        foreach (var realm in realms)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outputPath = Path.Combine(outputDirectory, $"found-guilds-{realm.Key}-len{nameLength}.txt");
            Console.WriteLine($"Scanning {realm.DisplayRealm} ({totalCombinations:N0} combinations)");
            Console.WriteLine($"  Output: {outputPath}");

            long checkedCount = 0;
            long foundCount = 0;
            long failedCount = 0;

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
                        var lookup = await _apiClient.LookupAsync(candidate, realm.ApiRealm, token);
                        var current = Interlocked.Increment(ref checkedCount);

                        if (lookup == GuildLookupStatus.Found)
                        {
                            Interlocked.Increment(ref foundCount);
                            channel.Writer.TryWrite(candidate);
                            Console.WriteLine($"  [FOUND] {candidate} on {realm.DisplayRealm}");
                        }
                        else if (lookup == GuildLookupStatus.Failed)
                        {
                            Interlocked.Increment(ref failedCount);
                        }

                        if (current % 1000 == 0 || current == totalCombinations)
                        {
                            Console.WriteLine(
                                $"  [{realm.DisplayRealm}] {current:N0}/{totalCombinations:N0} " +
                                $"({current * 100.0 / totalCombinations:F1}%) — " +
                                $"found: {Interlocked.Read(ref foundCount)}, failed: {Interlocked.Read(ref failedCount)}");
                        }
                    });
            }
            finally
            {
                channel.Writer.TryComplete();
                await writerTask;
            }

            Console.WriteLine(
                $"Finished {realm.DisplayRealm}: {foundCount} guild(s) discovered, " +
                $"{failedCount} request(s) failed after retries.");
            Console.WriteLine();
        }
    }

    public async Task ScanDictionaryAsync(
        IReadOnlyList<string> candidates,
        string dictionaryKey,
        string? realmFilter,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentException.ThrowIfNullOrWhiteSpace(dictionaryKey);
        var realms = ResolveRealms(realmFilter);
        var outputSuffix = dictionaryKey.Equals("hu", StringComparison.OrdinalIgnoreCase)
            ? "dictionary"
            : $"{dictionaryKey.ToLowerInvariant()}-dictionary";

        foreach (var realm in realms)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var foundPath = Path.Combine(outputDirectory, $"found-guilds-{realm.Key}-{outputSuffix}.txt");
            var checkedPath = Path.Combine(outputDirectory, $"checked-guilds-{realm.Key}-{outputSuffix}.txt");
            var foundCandidates = LoadCandidateSet(foundPath);
            var completedCandidates = LoadCandidateSet(checkedPath);
            completedCandidates.UnionWith(foundCandidates);

            var pendingCandidates = candidates
                .Where(candidate => !completedCandidates.Contains(candidate))
                .ToArray();

            Console.WriteLine($"Scanning dictionary candidates on {realm.DisplayRealm}");
            Console.WriteLine($"  Total candidates : {candidates.Count:N0}");
            Console.WriteLine($"  Already checked  : {candidates.Count - pendingCandidates.Length:N0}");
            Console.WriteLine($"  Remaining        : {pendingCandidates.Length:N0}");
            Console.WriteLine($"  Found output     : {foundPath}");
            Console.WriteLine($"  Resume checkpoint: {checkedPath}");

            if (pendingCandidates.Length == 0)
            {
                Console.WriteLine("  Nothing left to scan.");
                Console.WriteLine();
                continue;
            }

            long processedCount = 0;
            long completedCount = 0;
            long foundCount = 0;
            long failedCount = 0;

            var channel = Channel.CreateUnbounded<DictionaryScanResult>(
                new UnboundedChannelOptions { SingleReader = true });
            var writerTask = WriteDictionaryResultsAsync(
                foundPath,
                checkedPath,
                foundCandidates,
                channel.Reader);

            try
            {
                await Parallel.ForEachAsync(
                    pendingCandidates,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = _maxConcurrent,
                        CancellationToken = cancellationToken
                    },
                    async (candidate, token) =>
                    {
                        var lookup = await _apiClient.LookupAsync(candidate, realm.ApiRealm, token);
                        var processed = Interlocked.Increment(ref processedCount);

                        if (lookup == GuildLookupStatus.Failed)
                        {
                            Interlocked.Increment(ref failedCount);
                        }
                        else
                        {
                            Interlocked.Increment(ref completedCount);
                            channel.Writer.TryWrite(new DictionaryScanResult(candidate, lookup));

                            if (lookup == GuildLookupStatus.Found)
                            {
                                Interlocked.Increment(ref foundCount);
                                Console.WriteLine($"  [FOUND] {candidate} on {realm.DisplayRealm}");
                            }
                        }

                        if (processed % 250 == 0 || processed == pendingCandidates.Length)
                        {
                            Console.WriteLine(
                                $"  [{realm.DisplayRealm}] {processed:N0}/{pendingCandidates.Length:N0} " +
                                $"({processed * 100.0 / pendingCandidates.Length:F1}%) — " +
                                $"found: {Interlocked.Read(ref foundCount)}, failed: {Interlocked.Read(ref failedCount)}");
                        }
                    });
            }
            finally
            {
                channel.Writer.TryComplete();
                await writerTask;
            }

            Console.WriteLine(
                $"Finished {realm.DisplayRealm}: {completedCount:N0} candidate(s) checkpointed, " +
                $"{foundCount:N0} guild(s) found, {failedCount:N0} candidate(s) left for retry.");
            Console.WriteLine();
        }
    }

    private static async Task WriteFoundGuildsAsync(string outputPath, ChannelReader<string> reader)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        await using var stream = new FileStream(
            outputPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            4096,
            useAsync: true);
        await using var writer = new StreamWriter(stream, encoding);

        await foreach (var guild in reader.ReadAllAsync())
        {
            await writer.WriteLineAsync(guild);
            await writer.FlushAsync();
        }
    }

    private static async Task WriteDictionaryResultsAsync(
        string foundPath,
        string checkedPath,
        HashSet<string> foundCandidates,
        ChannelReader<DictionaryScanResult> reader)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        await using var foundStream = new FileStream(
            foundPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            4096,
            useAsync: true);
        await using var checkedStream = new FileStream(
            checkedPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            4096,
            useAsync: true);
        await using var foundWriter = new StreamWriter(foundStream, encoding);
        await using var checkedWriter = new StreamWriter(checkedStream, encoding);

        var unflushedCount = 0;
        await foreach (var result in reader.ReadAllAsync())
        {
            if (result.Status == GuildLookupStatus.Found && foundCandidates.Add(result.Candidate))
            {
                await foundWriter.WriteLineAsync(result.Candidate);
            }

            await checkedWriter.WriteLineAsync(result.Candidate);
            unflushedCount++;

            if (unflushedCount >= 100)
            {
                await foundWriter.FlushAsync();
                await checkedWriter.FlushAsync();
                unflushedCount = 0;
            }
        }

        await foundWriter.FlushAsync();
        await checkedWriter.FlushAsync();
    }

    private static HashSet<string> LoadCandidateSet(string path) =>
        File.Exists(path)
            ? new HashSet<string>(
                File.ReadLines(path)
                    .Select(candidate => candidate.Trim())
                    .Where(candidate => candidate.Length > 0),
                StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static RealmInfo[] ResolveRealms(string? realmFilter) =>
        realmFilter is null
            ? AllRealms
            : AllRealms.Where(realm => realm.Key == realmFilter).ToArray();

    private static IEnumerable<string> GenerateCombinations(int length)
    {
        var indices = new int[length];
        var chars = new char[length];

        while (true)
        {
            for (var index = 0; index < length; index++)
            {
                chars[index] = Alphabet[indices[index]];
            }

            yield return new string(chars);

            var position = length - 1;
            while (position >= 0)
            {
                indices[position]++;
                if (indices[position] < Alphabet.Length) break;
                indices[position] = 0;
                position--;
            }

            if (position < 0) break;
        }
    }

    private static bool IsValidGuildName(string name) =>
        name[0] != ' ' && name[^1] != ' ' && !name.Contains("  ");

    private static long CountValidCombinations(int length)
    {
        if (length == 0) return 0;

        const long letterCount = 26;
        long endsInLetter = letterCount;
        long endsInSpace = 0;
        for (var index = 1; index < length; index++)
        {
            (endsInLetter, endsInSpace) =
                ((endsInLetter + endsInSpace) * letterCount, endsInLetter);
        }

        return endsInLetter;
    }

    public void Dispose() => _apiClient.Dispose();

    private sealed record DictionaryScanResult(string Candidate, GuildLookupStatus Status);
    private sealed record RealmInfo(string Key, string ApiRealm, string DisplayRealm);
}
