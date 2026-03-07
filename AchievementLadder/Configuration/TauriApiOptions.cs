namespace AchievementLadder.Configuration;

public sealed class TauriApiOptions
{
    private const string PlaceholderApiKey = "YOUR_REAL_API_KEY_HERE";
    private const string PlaceholderSecret = "YOUR_REAL_SECRET_HERE";

    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;

    public void ApplyEnvironmentOverrides()
    {
        BaseUrl = FirstNonEmpty(
            Environment.GetEnvironmentVariable("TAURI_API_BASEURL"),
            Environment.GetEnvironmentVariable("TauriApi__BaseUrl"),
            BaseUrl);

        ApiKey = FirstNonEmpty(
            Environment.GetEnvironmentVariable("TAURI_API_APIKEY"),
            Environment.GetEnvironmentVariable("TauriApi__ApiKey"),
            ApiKey);

        Secret = FirstNonEmpty(
            Environment.GetEnvironmentVariable("TAURI_API_SECRET"),
            Environment.GetEnvironmentVariable("TauriApi__Secret"),
            Secret);
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            throw new InvalidOperationException("Missing TauriApi.BaseUrl in appsettings.json or environment variables.");
        }

        if (string.IsNullOrWhiteSpace(ApiKey) || ApiKey.Contains(PlaceholderApiKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Missing a real TauriApi.ApiKey. Update appsettings.json or set TAURI_API_APIKEY.");
        }

        if (string.IsNullOrWhiteSpace(Secret) || Secret.Contains(PlaceholderSecret, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Missing a real TauriApi.Secret. Update appsettings.json or set TAURI_API_SECRET.");
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }
}
