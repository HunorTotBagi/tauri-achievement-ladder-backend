using System.Text.Json;

namespace AchievementLadder.Configuration;

public sealed class AppSettings
{
    public TauriApiOptions TauriApi { get; set; } = new();

    public static AppSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Could not find configuration file: {path}");
        }

        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<AppSettings>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new AppSettings();

        settings.TauriApi.ApplyEnvironmentOverrides();
        settings.TauriApi.Validate();

        return settings;
    }
}
