using System.Net;
using System.Text;
using System.Text.Json;
using AchievementLadder.Configuration;

namespace AchievementLadder.Infrastructure;

public sealed class TauriApiClient : IDisposable
{
    private const int MaxRetryDelayMilliseconds = 15_000;

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _requestGate;
    private readonly string _apiUrl;
    private readonly string _secret;
    private readonly TimeSpan _requestTimeout;
    private readonly TimeSpan _initialRetryDelay;
    private readonly int _maxRetryAttempts;

    public TauriApiClient(TauriApiOptions options)
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

    public async Task<TauriApiResponseResult> FetchResponseElementAsync(
        string endpoint,
        object parameters,
        string requestLabel,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestLabel);

        var payload = JsonSerializer.Serialize(new
        {
            secret = _secret,
            url = endpoint,
            @params = parameters
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

                if (!response.IsSuccessStatusCode)
                {
                    lastFailure = $"API returned {(int)response.StatusCode} {response.ReasonPhrase}";
                    shouldRetry = IsTransientStatusCode(response.StatusCode);
                    retryDelay = GetRetryDelay(response, attempt);
                }
                else
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(timeoutSource.Token);
                    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeoutSource.Token);

                    if (document.RootElement.TryGetProperty("response", out var responseElement))
                    {
                        return TauriApiResponseResult.Success(responseElement.Clone());
                    }

                    lastFailure = "Missing response payload.";
                    shouldRetry = true;
                    retryDelay = GetRetryDelay(attempt);
                }
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

        Console.Error.WriteLine($"[{endpoint}] Skipping {requestLabel}: {lastFailure ?? "Unknown failure."}");
        return TauriApiResponseResult.Failure(lastFailure ?? "Unknown failure.");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _requestGate.Dispose();
    }

    private TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is { } headerDelay && headerDelay > TimeSpan.Zero)
        {
            return headerDelay;
        }

        return GetRetryDelay(attempt);
    }

    private TimeSpan GetRetryDelay(int attempt)
    {
        var exponentialDelay = _initialRetryDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var jitterMilliseconds = Random.Shared.Next(125, 500);
        var totalMilliseconds = Math.Min(MaxRetryDelayMilliseconds, exponentialDelay + jitterMilliseconds);
        return TimeSpan.FromMilliseconds(totalMilliseconds);
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
}

public readonly record struct TauriApiResponseResult(
    bool Succeeded,
    JsonElement? ResponseElement,
    string? FailureMessage)
{
    public static TauriApiResponseResult Success(JsonElement responseElement) =>
        new(true, responseElement, null);

    public static TauriApiResponseResult Failure(string failureMessage) =>
        new(false, null, failureMessage);
}
