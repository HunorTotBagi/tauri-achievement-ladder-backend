using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Tauri.Core.Configuration;
using Tauri.Core.Infrastructure;
using RealmFirstAchievements.Models;

namespace RealmFirstAchievements.Services;

public sealed class RealmFirstAchievementExportService(
    string achievementLadderDataDirectory,
    TauriApiOptions apiOptions,
    ITauriApiClient apiClient
)
{
    private const string Endpoint = "achievement-firsts";
    private const string CharacterSheetEndpoint = "character-sheet";
    private const string ValidCharactersFileName = "valid-realm-first-characters.txt";

    private static readonly RealmSource[] RealmSources =
    [
        new("Evermoon", "[EN] Evermoon"),
        new("Tauri", "[HU] Tauri WoW Server"),
        new("WoD", "[HU] Warriors of Darkness"),
    ];

    private readonly string _achievementLadderDataDirectory = Path.GetFullPath(
        achievementLadderDataDirectory
    );
    private readonly TauriApiOptions _apiOptions = apiOptions;
    private readonly ITauriApiClient _apiClient = apiClient;

    public async Task<RealmFirstAchievementExportResult> ExportAsync(
        CancellationToken cancellationToken
    )
    {
        var candidateCharacters = new ConcurrentBag<CharacterCandidate>();

        await Parallel.ForEachAsync(
            RealmSources,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Min(
                    RealmSources.Length,
                    Math.Max(1, _apiOptions.MaxConcurrentRequests)
                ),
                CancellationToken = cancellationToken,
            },
            async (realm, ct) =>
            {
                Console.WriteLine($"Fetching {realm.DisplayName}...");

                var result = await _apiClient.FetchResponseElementAsync(
                    Endpoint,
                    new { r = realm.ApiName },
                    realm.DisplayName,
                    ct
                );

                if (!result.Succeeded || result.ResponseElement is not { } response)
                {
                    throw new InvalidOperationException(
                        $"Could not export {realm.DisplayName}: {result.FailureMessage ?? "No response payload."}"
                    );
                }

                foreach (var characterName in ExtractCharacterNames(response))
                {
                    candidateCharacters.Add(
                        new CharacterCandidate(characterName, realm.ApiName, realm.DisplayName)
                    );
                }
            }
        );

        var orderedCandidateCharacters = candidateCharacters
            .Distinct(new CharacterCandidateComparer())
            .OrderBy(character => character.DisplayRealm, StringComparer.OrdinalIgnoreCase)
            .ThenBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var validCharacters = await ValidateCharactersAsync(
            _apiClient,
            orderedCandidateCharacters,
            cancellationToken
        );
        var validCharactersPath = await WriteValidCharactersAsync(
            _achievementLadderDataDirectory,
            validCharacters,
            cancellationToken
        );

        return new RealmFirstAchievementExportResult(
            validCharactersPath,
            orderedCandidateCharacters.Count,
            validCharacters.Count
        );
    }

    private async Task<IReadOnlyList<CharacterCandidate>> ValidateCharactersAsync(
        ITauriApiClient apiClient,
        IReadOnlyList<CharacterCandidate> characters,
        CancellationToken cancellationToken
    )
    {
        if (characters.Count == 0)
        {
            return [];
        }

        var validCharacters = new List<CharacterCandidate>();
        var validCharactersLock = new Lock();
        var processedCount = 0;

        await Parallel.ForEachAsync(
            characters,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, _apiOptions.MaxConcurrentRequests),
                CancellationToken = cancellationToken,
            },
            async (character, ct) =>
            {
                if (await CharacterExistsAsync(apiClient, character, ct))
                {
                    lock (validCharactersLock)
                    {
                        validCharacters.Add(character);
                    }
                }

                var processed = Interlocked.Increment(ref processedCount);
                if (processed % 25 == 0 || processed == characters.Count)
                {
                    Console.Write($"\rValidated characters: {processed}/{characters.Count}");
                }
            }
        );

        Console.WriteLine();

        return validCharacters
            .OrderBy(character => character.DisplayRealm, StringComparer.OrdinalIgnoreCase)
            .ThenBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<bool> CharacterExistsAsync(
        ITauriApiClient apiClient,
        CharacterCandidate character,
        CancellationToken cancellationToken
    )
    {
        var result = await apiClient.FetchResponseElementAsync(
            CharacterSheetEndpoint,
            new { r = character.ApiRealm, n = character.Name },
            $"{character.Name}-{character.DisplayRealm}",
            cancellationToken
        );

        if (!result.Succeeded || result.ResponseElement is not { } response)
        {
            return false;
        }

        return !IsEmptyResponse(response);
    }

    private static async Task<string> WriteValidCharactersAsync(
        string outputDirectory,
        IReadOnlyList<CharacterCandidate> characters,
        CancellationToken cancellationToken
    )
    {
        Directory.CreateDirectory(outputDirectory);

        var outputPath = Path.Combine(outputDirectory, ValidCharactersFileName);
        var temporaryPath = outputPath + ".tmp";
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        await using (
            var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                useAsync: true
            )
        )
        await using (var writer = new StreamWriter(stream, encoding))
        {
            for (var index = 0; index < characters.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var character = characters[index];

                await writer.WriteAsync($"{character.Name}-{character.DisplayRealm}");

                if (index < characters.Count - 1)
                {
                    await writer.WriteLineAsync();
                }
            }
        }

        File.Move(temporaryPath, outputPath, overwrite: true);
        return outputPath;
    }

    private static IEnumerable<string> ExtractCharacterNames(JsonElement response)
    {
        if (!TryGetAchievementFirsts(response, out var achievementFirsts))
        {
            yield break;
        }

        foreach (var achievementProperty in achievementFirsts.EnumerateObject())
        {
            if (achievementProperty.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var entry in achievementProperty.Value.EnumerateArray())
            {
                if (
                    entry.ValueKind != JsonValueKind.Object
                    || !entry.TryGetProperty("charname", out var charNameProperty)
                    || charNameProperty.ValueKind != JsonValueKind.String
                )
                {
                    continue;
                }

                var characterName = charNameProperty.GetString()?.Trim();
                if (IsUsableCharacterName(characterName))
                {
                    yield return characterName!;
                }
            }
        }
    }

    private static bool TryGetAchievementFirsts(
        JsonElement response,
        out JsonElement achievementFirsts
    )
    {
        if (
            response.ValueKind == JsonValueKind.Object
            && response.TryGetProperty("achievementFirsts", out achievementFirsts)
            && achievementFirsts.ValueKind == JsonValueKind.Object
        )
        {
            return true;
        }

        achievementFirsts = response;
        return response.ValueKind == JsonValueKind.Object;
    }

    private static bool IsUsableCharacterName(string? characterName) =>
        !string.IsNullOrWhiteSpace(characterName)
        && !characterName.Contains('#', StringComparison.Ordinal);

    private static bool IsEmptyResponse(JsonElement responseElement)
    {
        return responseElement.ValueKind switch
        {
            JsonValueKind.Undefined => true,
            JsonValueKind.Null => true,
            JsonValueKind.Object => !responseElement.EnumerateObject().Any(),
            JsonValueKind.Array => responseElement.GetArrayLength() == 0,
            JsonValueKind.String => string.IsNullOrWhiteSpace(responseElement.GetString()),
            _ => false,
        };
    }

    private sealed record RealmSource(string DisplayName, string ApiName);

    private sealed record CharacterCandidate(string Name, string ApiRealm, string DisplayRealm);

    private sealed class CharacterCandidateComparer : IEqualityComparer<CharacterCandidate>
    {
        public bool Equals(CharacterCandidate? x, CharacterCandidate? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    x.DisplayRealm,
                    y.DisplayRealm,
                    StringComparison.OrdinalIgnoreCase
                );
        }

        public int GetHashCode(CharacterCandidate obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.DisplayRealm)
            );
        }
    }
}
