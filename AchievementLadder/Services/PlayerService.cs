using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using AchievementLadder.Configuration;
using AchievementLadder.Helpers;
using AchievementLadder.Infrastructure;
using AchievementLadder.Models;
using RareAchiAndItemScan;

namespace AchievementLadder.Services;

public class PlayerService(string projectRoot, TauriApiOptions apiOptions, PlayerCsvStore csvStore)
{
    private static readonly CharacterTargetComparer CharacterComparer = new();
    private const int ProgressInterval = 250;
    private const int ProgressBarWidth = 30;
    private readonly int _characterWorkerCount = Math.Max(4, apiOptions.MaxConcurrentRequests * 2);
    private static readonly IReadOnlyList<RareAchievementDefinition> RareAchievementDefinitions = RareScanCatalog.RareAchievementNames
        .Select(entry => new RareAchievementDefinition(entry.Key, entry.Value))
        .ToList();

    public async Task<SyncResult> SyncDataAsync(CancellationToken cancellationToken)
    {
        using var apiClient = new TauriApiClient(apiOptions);

        var solutionRoot = ProjectPaths.FindSolutionRoot(projectRoot);
        var retryOutputPath = Path.Combine(solutionRoot, "MissingPlayersToScan.txt");
        var allCharacters = new List<(string Name, string ApiRealm, string DisplayRealm)>();
        LoadCharacterSources(allCharacters);

        var distinctCharacters = allCharacters
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.Name) &&
                !x.Name.Contains('#', StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(x.ApiRealm) &&
                !string.IsNullOrWhiteSpace(x.DisplayRealm))
            .Distinct(CharacterComparer)
            .ToList();

        Console.WriteLine($"Scanning {distinctCharacters.Count} characters...");
        Console.WriteLine(
            $"API settings: concurrency={apiOptions.MaxConcurrentRequests}, timeout={apiOptions.RequestTimeoutSeconds}s, retries={apiOptions.MaxRetryAttempts}");
        WriteProgress(0, distinctCharacters.Count);

        var players = new ConcurrentBag<Player>();
        var rareAchievementEntries = new ConcurrentBag<CharacterRareAchievementEntry>();
        var retryCharacters = new ConcurrentBag<(string Name, string ApiRealm, string DisplayRealm)>();
        var totalCharacters = distinctCharacters.Count;
        var done = 0;
        var progressLock = new Lock();

        await Parallel.ForEachAsync(
            distinctCharacters,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _characterWorkerCount,
                CancellationToken = cancellationToken
            },
            async (character, ct) =>
            {
                var syncResult = await FetchCharacterSyncAsync(
                    apiClient,
                    character.Name,
                    character.ApiRealm,
                    character.DisplayRealm,
                    ct);

                if (syncResult.Player is { } player)
                {
                    players.Add(player);

                    if (syncResult.RareAchievements.Count > 0)
                    {
                        rareAchievementEntries.Add(new CharacterRareAchievementEntry(
                            player.Name,
                            player.Realm,
                            player.Race,
                            player.Gender,
                            player.Class,
                            player.Guild,
                            syncResult.RareAchievements));
                    }
                }

                if (!syncResult.IsFullySuccessful)
                {
                    retryCharacters.Add(character);
                }

                var processed = Interlocked.Increment(ref done);
                if (processed % ProgressInterval == 0 || processed == totalCharacters)
                {
                    lock (progressLock)
                    {
                        WriteProgress(processed, totalCharacters);
                    }
                }
            });

        if (totalCharacters > 0)
        {
            Console.WriteLine();
        }

        var orderedPlayers = players
            .OrderByDescending(player => player.AchievementPoints)
            .ThenByDescending(player => player.HonorableKills)
            .ThenBy(player => player.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(player => player.Realm, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var orderedRareAchievementEntries = rareAchievementEntries
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Realm, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var orderedRetryCharacters = retryCharacters
            .Distinct(CharacterComparer)
            .OrderBy(character => character.DisplayRealm, StringComparer.OrdinalIgnoreCase)
            .ThenBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var generatedAt = DateTimeOffset.UtcNow;

        var playersCsvPath = await csvStore.WriteAsync(orderedPlayers, "Players.csv", cancellationToken);
        var rareAchievementsPath = await csvStore.WriteJsonAsync(
            "RareAchievements.json",
            new RareAchievementExport(
                generatedAt,
                RareAchievementDefinitions,
                orderedRareAchievementEntries),
            cancellationToken);
        var lastUpdatedPath = await csvStore.WriteTextAsync(
            "lastUpdated.txt",
            generatedAt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            cancellationToken);

        await WriteRetryCharactersAsync(retryOutputPath, orderedRetryCharacters, cancellationToken);

        return new SyncResult(
            orderedPlayers.Count,
            orderedRetryCharacters.Count,
            playersCsvPath,
            rareAchievementsPath,
            lastUpdatedPath,
            retryOutputPath);

        void LoadCharacterSources(List<(string Name, string ApiRealm, string DisplayRealm)> output)
        {
            CharacterHelpers.LoadDefaultCharacterSources(
                projectRoot,
                output,
                includePvPSeasonCharacters: true,
                includeRealmFirstCharacters: true);
        }
    }

    private static async Task<CharacterSyncResult> FetchCharacterSyncAsync(
        TauriApiClient apiClient,
        string name,
        string apiRealm,
        string displayRealm,
        CancellationToken ct)
    {
        var responseResult = await apiClient.FetchResponseElementAsync(
            "character-achievements",
            new { r = apiRealm, n = name },
            $"{name}-{displayRealm}",
            ct);

        if (!responseResult.Succeeded || responseResult.ResponseElement is not { } response)
        {
            return CharacterSyncResult.Failure();
        }

        var player = CharacterResponseMapper.CreatePlayer(response, name, displayRealm);
        var rareAchievements = RareAchievementExtractor.ExtractRareAchievements(response, RareAchievementDefinitions);

        return CharacterSyncResult.Success(player, rareAchievements);
    }

    private static async Task WriteRetryCharactersAsync(
        string outputPath,
        IReadOnlyList<(string Name, string ApiRealm, string DisplayRealm)> characters,
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
            foreach (var character in characters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteLineAsync($"{character.Name}-{character.DisplayRealm}");
            }
        }

        File.Move(tempPath, outputPath, overwrite: true);
    }

    private static void WriteProgress(int processed, int total)
    {
        if (total <= 0)
        {
            Console.WriteLine("Progress: [------------------------------] 0/0 (100.0%)");
            return;
        }

        var ratio = (double)processed / total;
        var filledWidth = Math.Min(ProgressBarWidth, (int)Math.Round(ratio * ProgressBarWidth, MidpointRounding.AwayFromZero));
        var bar = new string('#', filledWidth) + new string('-', ProgressBarWidth - filledWidth);

        Console.Write($"\rProgress: [{bar}] {processed}/{total} ({ratio:P1})");
    }

    private readonly record struct CharacterSyncResult(
        Player? Player,
        IReadOnlyList<CharacterRareAchievement> RareAchievements,
        bool Succeeded)
    {
        public bool IsFullySuccessful => Succeeded;

        public static CharacterSyncResult Success(Player player, IReadOnlyList<CharacterRareAchievement> rareAchievements) =>
            new(player, rareAchievements, true);

        public static CharacterSyncResult Failure() =>
            new(null, Array.Empty<CharacterRareAchievement>(), false);
    }

    private sealed class CharacterTargetComparer : IEqualityComparer<(string Name, string ApiRealm, string DisplayRealm)>
    {
        public bool Equals(
            (string Name, string ApiRealm, string DisplayRealm) x,
            (string Name, string ApiRealm, string DisplayRealm) y) =>
            string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.DisplayRealm, y.DisplayRealm, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Name, string ApiRealm, string DisplayRealm) obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.DisplayRealm));
        }
    }
}
