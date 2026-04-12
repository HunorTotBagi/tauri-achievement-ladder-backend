namespace AchievementLadder.Configuration;

public sealed class TauriApiOptions
{
    private const string PlaceholderApiKey = "YOUR_REAL_API_KEY_HERE";
    private const string PlaceholderSecret = "YOUR_REAL_SECRET_HERE";
    private const int DefaultMaxConcurrentRequests = 20;
    private const int DefaultRequestTimeoutSeconds = 30;
    private const int DefaultMaxRetryAttempts = 5;
    private const int DefaultInitialRetryDelayMilliseconds = 750;

    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public int MaxConcurrentRequests { get; set; } = DefaultMaxConcurrentRequests;
    public int RequestTimeoutSeconds { get; set; } = DefaultRequestTimeoutSeconds;
    public int MaxRetryAttempts { get; set; } = DefaultMaxRetryAttempts;
    public int InitialRetryDelayMilliseconds { get; set; } = DefaultInitialRetryDelayMilliseconds;

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

        MaxConcurrentRequests = FirstPositiveInt(
            Environment.GetEnvironmentVariable("TAURI_API_MAX_CONCURRENT_REQUESTS"),
            Environment.GetEnvironmentVariable("TauriApi__MaxConcurrentRequests"),
            MaxConcurrentRequests,
            DefaultMaxConcurrentRequests);

        RequestTimeoutSeconds = FirstPositiveInt(
            Environment.GetEnvironmentVariable("TAURI_API_REQUEST_TIMEOUT_SECONDS"),
            Environment.GetEnvironmentVariable("TauriApi__RequestTimeoutSeconds"),
            RequestTimeoutSeconds,
            DefaultRequestTimeoutSeconds);

        MaxRetryAttempts = FirstPositiveInt(
            Environment.GetEnvironmentVariable("TAURI_API_MAX_RETRY_ATTEMPTS"),
            Environment.GetEnvironmentVariable("TauriApi__MaxRetryAttempts"),
            MaxRetryAttempts,
            DefaultMaxRetryAttempts);

        InitialRetryDelayMilliseconds = FirstPositiveInt(
            Environment.GetEnvironmentVariable("TAURI_API_INITIAL_RETRY_DELAY_MS"),
            Environment.GetEnvironmentVariable("TauriApi__InitialRetryDelayMilliseconds"),
            InitialRetryDelayMilliseconds,
            DefaultInitialRetryDelayMilliseconds);
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

        if (MaxConcurrentRequests <= 0)
        {
            throw new InvalidOperationException("TauriApi.MaxConcurrentRequests must be greater than zero.");
        }

        if (RequestTimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("TauriApi.RequestTimeoutSeconds must be greater than zero.");
        }

        if (MaxRetryAttempts <= 0)
        {
            throw new InvalidOperationException("TauriApi.MaxRetryAttempts must be greater than zero.");
        }

        if (InitialRetryDelayMilliseconds <= 0)
        {
            throw new InvalidOperationException("TauriApi.InitialRetryDelayMilliseconds must be greater than zero.");
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

    private static int FirstPositiveInt(
        string? environmentValue,
        string? configurationValue,
        int currentValue,
        int fallbackValue)
    {
        foreach (var value in new[] { environmentValue, configurationValue })
        {
            if (!string.IsNullOrWhiteSpace(value) &&
                int.TryParse(value.Trim(), out var parsedValue) &&
                parsedValue > 0)
            {
                return parsedValue;
            }
        }

        return currentValue > 0 ? currentValue : fallbackValue;
    }
}
