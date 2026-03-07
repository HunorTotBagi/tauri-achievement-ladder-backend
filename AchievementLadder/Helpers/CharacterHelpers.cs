using System.Text.Json;

namespace AchievementLadder.Helpers;

public static class CharacterHelpers
{
    private static readonly Dictionary<string, (string ApiRealm, string DisplayRealm)> Realms =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Evermoon"] = ("[EN] Evermoon", "Evermoon"),
            ["Tauri"] = ("[HU] Tauri WoW Server", "Tauri"),
            ["WoD"] = ("[HU] Warriors of Darkness", "WoD")
        };

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

            if (!Realms.TryGetValue(realmText, out var realm))
            {
                continue;
            }

            output.Add((name, realm.ApiRealm, realm.DisplayRealm));
        }
    }
}
