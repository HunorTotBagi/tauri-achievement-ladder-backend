using System.Net;
using System.Text;
using System.Text.Json;
using AchievementLadder.Configuration;

namespace GuildNameBruteForcer;

internal enum GuildLookupStatus
{
    NotFound,
    Found,
    Failed
}

internal sealed class GuildLookupClient : IDisposable
{
    private const int GuildNotFoundErrorCode = 13;
    private const int MaxRetryDelayMilliseconds = 15_000;

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _requestGate;
    private readonly string _apiUrl;
    private readonly string _secret;
    private readonly TimeSpan _requestTimeout;
    private readonly TimeSpan _initialRetryDelay;
    private readonly int _maxRetryAttempts;

    public GuildLookupClient(TauriApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _apiUrl = BuildApiUrl(options.BaseUrl, options.ApiKey);
        _secret = options.Secret;
        _requestTimeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
        _initialRetryDelay = TimeSpan.FromMilliseconds(options.InitialRetryDelayMilliseconds);
        _maxRetryAttempts = options.MaxRetryAttempts;
        _requestGate = new SemaphoreSlim(options.MaxConcurrentRequests, options.MaxConcurrentRequests);

        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            MaxConnectionsPerServer = options.MaxConcurrentRequests,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            UseCookies = false
        };

        _httpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public async Task<GuildLookupStatus> LookupAsync(
        string guildName,
        string apiRealm,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            secret = _secret,
            url = "guild-info",
            @params = new { r = apiRealm, gn = guildName }
        });

        string? lastFailure = null;

        for (var attempt = 1; attempt <= _maxRetryAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var shouldRetry = false;
            TimeSpan? retryDelay = null;

            await _requestGate.WaitAsync(cancellationToken);
            try
            {
                using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutSource.CancelAfter(_requestTimeout);

                using var request = new HttpRequestMessage(HttpMethod.Post, _apiUrl)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutSource.Token);

                var parsedResponse = await ParseResponseAsync(response, timeoutSource.Token);
                if (parsedResponse.Status is { } lookupStatus)
                {
                    return lookupStatus;
                }

                lastFailure = parsedResponse.FailureMessage
                    ?? $"API returned {(int)response.StatusCode} {response.ReasonPhrase}.";
                shouldRetry = IsTransientStatusCode(response.StatusCode);
                retryDelay = GetRetryDelay(response, attempt);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastFailure = $"Request timed out after {_requestTimeout.TotalSeconds:0} seconds.";
                shouldRetry = true;
                retryDelay = GetRetryDelay(attempt);
            }
            catch (HttpRequestException ex)
            {
                lastFailure = ex.Message;
                shouldRetry = true;
                retryDelay = GetRetryDelay(attempt);
            }
            catch (IOException ex)
            {
                lastFailure = $"{ex.GetType().Name}: {ex.Message}";
                shouldRetry = true;
                retryDelay = GetRetryDelay(attempt);
            }
            catch (JsonException ex)
            {
                lastFailure = $"Invalid JSON response: {ex.Message}";
                shouldRetry = true;
                retryDelay = GetRetryDelay(attempt);
            }
            finally
            {
                _requestGate.Release();
            }

            if (!shouldRetry || attempt == _maxRetryAttempts)
            {
                break;
            }

            await Task.Delay(retryDelay ?? GetRetryDelay(attempt), cancellationToken);
        }

        Console.Error.WriteLine(
            $"[guild-info] Could not check guild '{guildName}' on {apiRealm}: " +
            $"{lastFailure ?? "Unknown failure."}");
        return GuildLookupStatus.Failed;
    }

    private static async Task<ParsedGuildResponse> ParseResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException) when (!response.IsSuccessStatusCode)
        {
            return ParsedGuildResponse.Failed(
                $"API returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        using (document)
        {
            return ParseJsonResponse(response, document.RootElement);
        }
    }

    private static ParsedGuildResponse ParseJsonResponse(
        HttpResponseMessage response,
        JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return ParsedGuildResponse.Failed("API returned an unexpected JSON payload.");
        }

        if (root.TryGetProperty("errorcode", out var errorCodeElement) &&
            errorCodeElement.TryGetInt32(out var errorCode) &&
            errorCode == GuildNotFoundErrorCode)
        {
            return ParsedGuildResponse.Completed(GuildLookupStatus.NotFound);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = root.TryGetProperty("errorstring", out var errorElement) &&
                               errorElement.ValueKind == JsonValueKind.String
                ? errorElement.GetString()
                : null;
            return ParsedGuildResponse.Failed(
                $"API returned {(int)response.StatusCode} {response.ReasonPhrase}" +
                (string.IsNullOrWhiteSpace(errorMessage) ? "." : $": {errorMessage}"));
        }

        if (!root.TryGetProperty("response", out var responseElement) ||
            responseElement.ValueKind != JsonValueKind.Object)
        {
            return ParsedGuildResponse.Completed(GuildLookupStatus.NotFound);
        }

        var found = responseElement.TryGetProperty("guildList", out var guildList) &&
                    guildList.ValueKind == JsonValueKind.Object &&
                    guildList.EnumerateObject().Any();
        return ParsedGuildResponse.Completed(
            found ? GuildLookupStatus.Found : GuildLookupStatus.NotFound);
    }

    private TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        return retryAfter is { } headerDelay && headerDelay > TimeSpan.Zero
            ? headerDelay
            : GetRetryDelay(attempt);
    }

    private TimeSpan GetRetryDelay(int attempt)
    {
        var exponentialDelay = _initialRetryDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var jitterMilliseconds = Random.Shared.Next(125, 500);
        return TimeSpan.FromMilliseconds(
            Math.Min(MaxRetryDelayMilliseconds, exponentialDelay + jitterMilliseconds));
    }

    private static bool IsTransientStatusCode(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests or
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout;

    private static string BuildApiUrl(string baseUrl, string apiKey)
    {
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseUrl}{separator}apikey={Uri.EscapeDataString(apiKey)}";
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _requestGate.Dispose();
    }

    private readonly record struct ParsedGuildResponse(
        GuildLookupStatus? Status,
        string? FailureMessage)
    {
        public static ParsedGuildResponse Completed(GuildLookupStatus status) => new(status, null);
        public static ParsedGuildResponse Failed(string failureMessage) => new(null, failureMessage);
    }
}
