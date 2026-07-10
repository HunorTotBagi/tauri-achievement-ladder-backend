using System.Text.Json;

namespace Tauri.Core.Configuration;

public sealed class AppSettings
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public TauriApiOptions TauriApi { get; set; } = new();

    public static AppSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Could not find configuration file: {path}");
        }

        var json = File.ReadAllText(path);
        var settings =
            JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();

        settings.TauriApi.ApplyEnvironmentOverrides();
        settings.TauriApi.Validate();

        return settings;
    }
}
