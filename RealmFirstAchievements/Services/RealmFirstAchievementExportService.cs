using System.Text.Json;
using System.Text;
using AchievementLadder.Configuration;
using AchievementLadder.Infrastructure;
using RealmFirstAchievements.Models;

namespace RealmFirstAchievements.Services;

public sealed class RealmFirstAchievementExportService(string frontendSourceDirectory, TauriApiOptions apiOptions)
{
    private const string Endpoint = "achievement-firsts";
    private const string CharacterSheetEndpoint = "character-sheet";
    private const string ValidCharactersFileName = "valid-realm-first-characters.txt";

    private static readonly RealmSource[] RealmSources =
    [
        new("Evermoon", "[EN] Evermoon"),
        new("Tauri", "[HU] Tauri WoW Server"),
        new("WoD", "[HU] Warriors of Darkness")
    ];

    private readonly string _frontendSourceDirectory = Path.GetFullPath(frontendSourceDirectory);
    private readonly TauriApiOptions _apiOptions = apiOptions;

    public async Task<RealmFirstAchievementExportResult> ExportAsync(CancellationToken cancellationToken)
    {
        using var apiClient = new TauriApiClient(_apiOptions);
        var candidateCharacters = new HashSet<CharacterCandidate>(new CharacterCandidateComparer());

        foreach (var realm in RealmSources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Console.WriteLine($"Fetching {realm.DisplayName}...");

            var result = await apiClient.FetchResponseElementAsync(
                Endpoint,
                new { r = realm.ApiName },
                realm.DisplayName,
                cancellationToken);

            if (!result.Succeeded || result.ResponseElement is not { } response)
            {
                throw new InvalidOperationException(
                    $"Could not export {realm.DisplayName}: {result.FailureMessage ?? "No response payload."}");
            }

            foreach (var characterName in ExtractCharacterNames(response))
            {
                candidateCharacters.Add(new CharacterCandidate(
                    characterName,
                    realm.ApiName,
                    realm.DisplayName));
            }
        }

        var orderedCandidateCharacters = candidateCharacters
            .OrderBy(character => character.DisplayRealm, StringComparer.OrdinalIgnoreCase)
            .ThenBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var validCharacters = await ValidateCharactersAsync(orderedCandidateCharacters, cancellationToken);
        var frontendValidCharactersPath = await WriteValidCharactersAsync(
            _frontendSourceDirectory,
            validCharacters,
            cancellationToken);

        return new RealmFirstAchievementExportResult(
            frontendValidCharactersPath,
            orderedCandidateCharacters.Count,
            validCharacters.Count);
    }

    private async Task<IReadOnlyList<CharacterCandidate>> ValidateCharactersAsync(
        IReadOnlyList<CharacterCandidate> characters,
        CancellationToken cancellationToken)
    {
        if (characters.Count == 0)
        {
            return [];
        }

        var apiUrl = BuildApiUrl(_apiOptions.BaseUrl, _apiOptions.ApiKey);
        var validCharacters = new List<CharacterCandidate>();
        var validCharactersLock = new Lock();
        var processedCount = 0;

        using var httpClient = new HttpClient();

        await Parallel.ForEachAsync(
            characters,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, _apiOptions.MaxConcurrentRequests),
                CancellationToken = cancellationToken
            },
            async (character, ct) =>
            {
                if (await CharacterExistsAsync(httpClient, apiUrl, character, ct))
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
            });

        Console.WriteLine();

        return validCharacters
            .OrderBy(character => character.DisplayRealm, StringComparer.OrdinalIgnoreCase)
            .ThenBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<bool> CharacterExistsAsync(
        HttpClient httpClient,
        string apiUrl,
        CharacterCandidate character,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            secret = _apiOptions.Secret,
            url = CharacterSheetEndpoint,
            @params = new
            {
                r = character.ApiRealm,
                n = character.Name
            }
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        try
        {
            using var response = await httpClient.PostAsync(apiUrl, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            return document.RootElement.TryGetProperty("response", out var responseElement) &&
                   !IsEmptyResponse(responseElement);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Could not validate {character.Name}-{character.DisplayRealm}: {ex.Message}");
            return false;
        }
    }

    private static async Task<string> WriteValidCharactersAsync(
        string outputDirectory,
        IReadOnlyList<CharacterCandidate> characters,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        var outputPath = Path.Combine(outputDirectory, ValidCharactersFileName);
        var temporaryPath = outputPath + ".tmp";
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         64 * 1024,
                         useAsync: true))
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
                if (entry.ValueKind != JsonValueKind.Object ||
                    !entry.TryGetProperty("charname", out var charNameProperty) ||
                    charNameProperty.ValueKind != JsonValueKind.String)
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

    private static bool TryGetAchievementFirsts(JsonElement response, out JsonElement achievementFirsts)
    {
        if (response.ValueKind == JsonValueKind.Object &&
            response.TryGetProperty("achievementFirsts", out achievementFirsts) &&
            achievementFirsts.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        achievementFirsts = response;
        return response.ValueKind == JsonValueKind.Object;
    }

    private static bool IsUsableCharacterName(string? characterName) =>
        !string.IsNullOrWhiteSpace(characterName) &&
        !characterName.Contains('#', StringComparison.Ordinal);

    private static bool IsEmptyResponse(JsonElement responseElement)
    {
        return responseElement.ValueKind switch
        {
            JsonValueKind.Undefined => true,
            JsonValueKind.Null => true,
            JsonValueKind.Object => !responseElement.EnumerateObject().Any(),
            JsonValueKind.Array => responseElement.GetArrayLength() == 0,
            JsonValueKind.String => string.IsNullOrWhiteSpace(responseElement.GetString()),
            _ => false
        };
    }

    private static string BuildApiUrl(string baseUrl, string apiKey)
    {
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseUrl}{separator}apikey={Uri.EscapeDataString(apiKey)}";
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

            return string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(x.DisplayRealm, y.DisplayRealm, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(CharacterCandidate obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.DisplayRealm));
        }
    }
}
