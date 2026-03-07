using System.Text;
using System.Text.Json;
using AchievementLadder.Dtos;

namespace AchievementLadder.Helpers;

public static class CharacterHelpers
{
    public static void LoadCharacters(string projectRoot, string fileName, string apiRealm, string displayRealm, List<(string Name, string ApiRealm, string DisplayRealm)> output)
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
                    output.Add((name, apiRealm, displayRealm));
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
        CancellationToken cancellationToken = default)
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
            var response = await client.PostAsync(apiUrl, content, cancellationToken);
            var responseString = await response.Content.ReadAsStringAsync();

            var guildInfo = JsonSerializer.Deserialize<GuildInfoResponse>(responseString);
            if (guildInfo?.response?.guildList == null)
                return;

            foreach (var member in guildInfo.response.guildList.Values)
            {
                if (member.level >= 90)
                    output.Add((member.name, apiRealm, displayRealm));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch { }
    }
}
