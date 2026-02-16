using System.Text;
using System.Text.Json;
using AchievementLadder.Dtos;

namespace AchievementLadder.Helpers;

public static class CharacterHelpers
{
    public static void LoadCharacters(
        string projectRoot,
        string fileName,
        string apiRealm,
        string displayRealm,
        List<(string Name, string ApiRealm, string DisplayRealm)> output,
        IDictionary<string, int>? classByCharacter = null)
    {
        var filePath = Path.Combine(projectRoot, "Data", "CharacterCollection", fileName);
        if (!File.Exists(filePath))
            return;

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
                    lock (output)
                    {
                        output.Add((name, apiRealm, displayRealm));
                    }

                    if (classByCharacter is not null && TryReadClass(item, out var classId))
                    {
                        var key = CreateCharacterKey(name, displayRealm);
                        lock (classByCharacter)
                        {
                            if (!classByCharacter.ContainsKey(key))
                                classByCharacter[key] = classId;
                        }
                    }
                }
            }
        }
    }

    public static async Task LoadGuildMembersLevel90Async(
        string guildName,
        string apiRealm,
        string displayRealm,
        string apiUrl,
        string secret,
        List<(string, string, string)> output,
        HttpClient client,
        IDictionary<string, int>? classByCharacter = null)
    {
        var body = new
        {
            secret = secret,
            url = "guild-info",
            @params = new { r = apiRealm, gn = guildName }
        };

        var jsonBody = JsonSerializer.Serialize(body);
        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        try
        {
            var response = await client.PostAsync(apiUrl, content);
            var responseString = await response.Content.ReadAsStringAsync();

            var guildInfo = JsonSerializer.Deserialize<GuildInfoResponse>(responseString);
            if (guildInfo?.response?.guildList == null)
                return;

            foreach (var member in guildInfo.response.guildList.Values)
            {
                if (member.level >= 90)
                {
                    lock (output)
                    {
                        output.Add((member.name, apiRealm, displayRealm));
                    }

                    if (classByCharacter is not null && member.@class > 0)
                    {
                        var key = CreateCharacterKey(member.name, displayRealm);
                        lock (classByCharacter)
                        {
                            if (!classByCharacter.ContainsKey(key))
                                classByCharacter[key] = member.@class;
                        }
                    }
                }
            }
        }
        catch { }
    }

    public static string CreateCharacterKey(string name, string displayRealm)
        => $"{name.Trim().ToLowerInvariant()}::{displayRealm.Trim().ToLowerInvariant()}";

    private static bool TryReadClass(JsonElement item, out int classId)
    {
        classId = 0;
        if (!item.TryGetProperty("class", out var classEl))
            return false;

        if (classEl.ValueKind == JsonValueKind.Number)
            return classEl.TryGetInt32(out classId);

        if (classEl.ValueKind == JsonValueKind.String &&
            int.TryParse(classEl.GetString(), out classId))
            return true;

        return false;
    }
}
