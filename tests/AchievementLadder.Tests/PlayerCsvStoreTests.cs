using AchievementLadder.Services;
using Tauri.Core.Models;

namespace AchievementLadder.Tests;

public sealed class PlayerCsvStoreTests : IDisposable
{
    private readonly string _outputDirectory = Path.Combine(
        Path.GetTempPath(),
        $"achievement-ladder-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task WriteAsync_QuotesCommasQuotesAndNewLines()
    {
        var store = new PlayerCsvStore(_outputDirectory);
        var player = new Player
        {
            Name = "Mage, \"The Great\"",
            Realm = "Evermoon",
            Guild = "First line\nSecond line",
            Faction = "Alliance",
        };

        var path = await store.WriteAsync([player], "Players.csv", CancellationToken.None);
        var content = await File.ReadAllTextAsync(path, CancellationToken.None);

        Assert.Contains("\"Mage, \"\"The Great\"\"\"", content);
        Assert.Contains("\"First line\nSecond line\"", content);
    }

    [Fact]
    public async Task WriteJsonAsync_UsesCamelCasePropertyNames()
    {
        var store = new PlayerCsvStore(_outputDirectory);

        var path = await store.WriteJsonAsync(
            "result.json",
            new { PlayerCount = 3 },
            CancellationToken.None
        );
        var content = await File.ReadAllTextAsync(path, CancellationToken.None);

        Assert.Equal("{\"playerCount\":3}", content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDirectory))
        {
            Directory.Delete(_outputDirectory, recursive: true);
        }
    }
}
