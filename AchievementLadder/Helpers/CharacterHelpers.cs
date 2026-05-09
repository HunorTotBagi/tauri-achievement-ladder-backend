using System.Text.Json;

namespace AchievementLadder.Helpers;

public static class CharacterHelpers
{
    private const string RealmFirstCharactersFileName = "valid-realm-first-characters.txt";

    private static readonly Dictionary<string, (string ApiRealm, string DisplayRealm)> Realms =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Evermoon"] = ("[EN] Evermoon", "Evermoon"),
            ["Tauri"] = ("[HU] Tauri WoW Server", "Tauri"),
            ["WoD"] = ("[HU] Warriors of Darkness", "WoD")
        };
    private static readonly (string FileName, string ApiRealm, string DisplayRealm)[] CharacterCollectionSources =
    [
        ("evermoon-achi.txt", "[EN] Evermoon", "Evermoon"),
        ("evermoon-hk.txt", "[EN] Evermoon", "Evermoon"),
        ("evermoon-playTime.txt", "[EN] Evermoon", "Evermoon"),
        ("tauri-achi.txt", "[HU] Tauri WoW Server", "Tauri"),
        ("tauri-hk.txt", "[HU] Tauri WoW Server", "Tauri"),
        ("tauri-playTime.txt", "[HU] Tauri WoW Server", "Tauri"),
        ("wod-achi.txt", "[HU] Warriors of Darkness", "WoD"),
        ("wod-hk.txt", "[HU] Warriors of Darkness", "WoD"),
        ("wod-playTime.txt", "[HU] Warriors of Darkness", "WoD")
    ];
    private static readonly (string RelativePath, string ApiRealm, string DisplayRealm)[] AdditionalTextSources =
    [
        (Path.Combine("..", "RareAchiAndItemScan", "Input", "tauri-ban-list.txt"), "[HU] Tauri WoW Server", "Tauri"),
        (Path.Combine("..", "RareAchiAndItemScan", "Input", "vengeful.txt"), "[HU] Tauri WoW Server", "Tauri")
    ];

    public static void LoadDefaultCharacterSources(
        string projectRoot,
        List<(string Name, string ApiRealm, string DisplayRealm)> output,
        bool ignoreMissingGuildCharacters = false,
        bool includePvPSeasonCharacters = false,
        bool includeRealmFirstCharacters = false)
    {
        if (ignoreMissingGuildCharacters)
        {
            try
            {
                LoadGuildCharacters(projectRoot, "GuildCharacters.txt", output);
            }
            catch (FileNotFoundException)
            {
            }
        }
        else
        {
            LoadGuildCharacters(projectRoot, "GuildCharacters.txt", output);
        }

        foreach (var source in CharacterCollectionSources)
        {
            LoadCharacters(projectRoot, source.FileName, source.ApiRealm, source.DisplayRealm, output);
        }

        foreach (var source in AdditionalTextSources)
        {
            LoadCharactersFromTextFile(projectRoot, source.RelativePath, source.ApiRealm, source.DisplayRealm, output);
        }

        if (includePvPSeasonCharacters)
        {
            LoadPvPSeasonCharacters(projectRoot, output);
        }

        if (includeRealmFirstCharacters)
        {
            LoadRealmFirstCharacters(projectRoot, output);
        }
    }

    public static void LoadCharacters(string projectRoot, string fileName, string apiRealm, string displayRealm, List<(string Name, string ApiRealm, string DisplayRealm)> output)
    {
        var filePath = Path.Combine(projectRoot, "Data", "CharacterCollection", fileName);
        if (!File.Exists(filePath))
        {
            return;
        }

        var content = File.ReadAllText(filePath);
        using var doc = JsonDocument.Parse(content);

        var array = doc.RootElement
            .EnumerateObject()
            .First()
            .Value;

        foreach (var item in array.EnumerateArray())
        {
            if (item.TryGetProperty("name", out var nameProp))
            {
                var name = nameProp.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    output.Add((name, apiRealm, displayRealm));
                }
            }
        }
    }

    public static void LoadCharactersFromTextFile(
        string projectRoot,
        string relativePath,
        string apiRealm,
        string displayRealm,
        List<(string Name, string ApiRealm, string DisplayRealm)> output)
    {
        var filePath = Path.GetFullPath(relativePath, projectRoot);
        if (!File.Exists(filePath))
        {
            return;
        }

        foreach (var rawLine in File.ReadLines(filePath))
        {
            if (TryExtractCharacterName(rawLine, out var name))
            {
                output.Add((name, apiRealm, displayRealm));
            }
        }
    }

    public static void LoadGuildCharacters(
        string projectRoot,
        string fileName,
        List<(string Name, string ApiRealm, string DisplayRealm)> output)
    {
        var filePath = Path.Combine(projectRoot, "Data", "GuildCharacters", fileName);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Could not find guild character source file: {filePath}", filePath);
        }

        foreach (var rawLine in File.ReadLines(filePath))
        {
            var line = rawLine?.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var separatorIndex = line.LastIndexOf('-');
            if (separatorIndex <= 0 || separatorIndex == line.Length - 1)
            {
                continue;
            }

            var name = line[..separatorIndex].Trim();
            var realmText = line[(separatorIndex + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(realmText))
            {
                continue;
            }

            if (!TryResolveRealm(realmText, out var apiRealm, out var displayRealm))
            {
                continue;
            }

            output.Add((name, apiRealm, displayRealm));
        }
    }

    public static void LoadPvPSeasonCharacters(
        string projectRoot,
        List<(string Name, string ApiRealm, string DisplayRealm)> output)
    {
        var directoryPath = Path.Combine(projectRoot, "Data", "PvPSeasonCharacters");
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        foreach (var filePath in Directory
                     .EnumerateFiles(directoryPath, "*.txt", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            LoadCharactersFromBatchFile(filePath, output);
        }
    }

    public static void LoadRealmFirstCharacters(
        string projectRoot,
        List<(string Name, string ApiRealm, string DisplayRealm)> output)
    {
        var filePath = Path.Combine(projectRoot, "Data", RealmFirstCharactersFileName);
        if (!File.Exists(filePath))
        {
            return;
        }

        LoadCharactersFromBatchFile(filePath, output);
    }

    public static bool TryResolveRealm(
        string? rawRealm,
        out string apiRealm,
        out string displayRealm)
    {
        apiRealm = string.Empty;
        displayRealm = string.Empty;

        if (string.IsNullOrWhiteSpace(rawRealm))
        {
            return false;
        }

        var realm = rawRealm.Trim();

        foreach (var entry in Realms)
        {
            if (!string.Equals(realm, entry.Key, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(realm, entry.Value.ApiRealm, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(realm, entry.Value.DisplayRealm, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            apiRealm = entry.Value.ApiRealm;
            displayRealm = entry.Value.DisplayRealm;
            return true;
        }

        return false;
    }

    public static bool TryExtractCharacterWithRealm(
        string? rawLine,
        out string name,
        out string apiRealm,
        out string displayRealm)
    {
        name = string.Empty;
        apiRealm = string.Empty;
        displayRealm = string.Empty;

        if (string.IsNullOrWhiteSpace(rawLine))
        {
            return false;
        }

        var line = rawLine.Trim();
        if (line.StartsWith('#'))
        {
            return false;
        }

        var separatorIndex = line.LastIndexOf('-');
        if (separatorIndex <= 0 || separatorIndex == line.Length - 1)
        {
            return false;
        }

        var candidateName = line[..separatorIndex].Trim();
        var rawRealm = line[(separatorIndex + 1)..].Trim();

        if (string.IsNullOrWhiteSpace(candidateName) ||
            candidateName.Contains('#', StringComparison.Ordinal) ||
            !TryResolveRealm(rawRealm, out apiRealm, out displayRealm))
        {
            name = string.Empty;
            return false;
        }

        name = candidateName;
        return true;
    }

    public static bool TryExtractCharacterName(string? rawLine, out string name)
    {
        name = string.Empty;

        if (string.IsNullOrWhiteSpace(rawLine))
        {
            return false;
        }

        var line = rawLine.Trim();
        if (line.StartsWith('#'))
        {
            return false;
        }

        var candidate = line;
        var pipeIndex = line.IndexOf('|');

        if (pipeIndex > 0)
        {
            var dashIndex = line.LastIndexOf('-', pipeIndex - 1, pipeIndex);
            if (dashIndex >= 0 && dashIndex < pipeIndex - 1)
            {
                candidate = line[(dashIndex + 1)..pipeIndex];
            }
        }

        candidate = candidate.Trim();
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Contains('#', StringComparison.Ordinal))
        {
            return false;
        }

        name = candidate;
        return true;
    }

    private static void LoadCharactersFromBatchFile(
        string filePath,
        List<(string Name, string ApiRealm, string DisplayRealm)> output)
    {
        foreach (var rawLine in File.ReadLines(filePath))
        {
            if (!TryExtractCharacterWithRealm(rawLine, out var name, out var apiRealm, out var displayRealm))
            {
                continue;
            }

            output.Add((name, apiRealm, displayRealm));
        }
    }
}
