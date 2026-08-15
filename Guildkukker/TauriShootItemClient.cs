using System.Collections.Concurrent;
using Tauri.Core.Infrastructure;

namespace Guildkukker;

public sealed class TauriShootItemClient : IDisposable
{
    private const string BaseUrl = "https://legion-shoot.tauri.hu/";
    private readonly ConcurrentDictionary<int, Task<LegendaryItem>> _items = new();
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    public Task<LegendaryItem> LoadAsync(
        LegendaryItem fallback,
        CancellationToken cancellationToken
    ) => _items.GetOrAdd(fallback.Id, _ => LoadCoreAsync(fallback, cancellationToken));

    private async Task<LegendaryItem> LoadCoreAsync(
        LegendaryItem fallback,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var xml = await _httpClient.GetStringAsync(
                $"{BaseUrl}?item={fallback.Id}&xml",
                cancellationToken
            );
            return LegendaryItemParser.ParseTooltipXml(xml, fallback);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TimeoutException)
        {
            Console.Error.WriteLine(
                $"[TauriShoot] Item {fallback.Id} metadata unavailable: {ex.Message}"
            );
            return fallback;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine(
                $"[TauriShoot] Item {fallback.Id} metadata timed out: {ex.Message}"
            );
            return fallback;
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
