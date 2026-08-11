using System.Text.Json;
using AchievementLadder.Services;
using Tauri.Core.Infrastructure;

namespace AchievementLadder.Tests;

public sealed class PlayerServiceTests
{
    private static readonly DateTimeOffset ScanStartedAt = new(2026, 8, 11, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FetchCharacterSyncAsync_AllEndpointsSucceed_ReturnsCompletePlayer()
    {
        var client = new FakeTauriApiClient(
            new Dictionary<string, TauriApiResponseResult>
            {
                ["character-achievements"] = Success(
                    """
                    {
                      "race": 1,
                      "gender": 0,
                      "class": 8,
                      "pts": 100,
                      "playerHonorKills": 25,
                      "faction_string_class": "Alliance",
                      "guildName": "Test Guild",
                      "Achievements": { "6": { "date": "2020-01-01" }, "416": {} }
                    }
                    """
                ),
                ["character-itemappearances"] = Success(
                    """{ "itemappearances": { "owned": [[1, 2], [3]] } }"""
                ),
                ["character-sheet-minimal"] = Success(
                    """{ "played_time": 9000, "achievements_total": 321 }"""
                ),
            }
        );

        var result = await PlayerService.FetchCharacterSyncAsync(
            client,
            "Examplemage",
            "[EN] Evermoon",
            "Evermoon",
            ScanStartedAt,
            CancellationToken.None
        );

        Assert.True(result.IsFullySuccessful);
        Assert.NotNull(result.Player);
        Assert.Equal("Examplemage", result.Player.Name);
        Assert.Equal(3, result.Player.AppearanceCount);
        Assert.Equal(9000, result.Player.PlayedTime);
        Assert.Equal(321, result.Player.AchievementsTotal);
        Assert.Contains(result.RareAchievements, achievement => achievement.Id == 416);
        Assert.Equal(
            ["character-achievements", "character-itemappearances", "character-sheet-minimal"],
            client.RequestedEndpoints
        );
    }

    [Fact]
    public async Task FetchCharacterSyncAsync_AchievementRequestFails_StopsWithoutPartialPlayer()
    {
        var client = new FakeTauriApiClient(
            new Dictionary<string, TauriApiResponseResult>
            {
                ["character-achievements"] = TauriApiResponseResult.Failure("Unavailable"),
            }
        );

        var result = await FetchAsync(client);

        Assert.False(result.IsFullySuccessful);
        Assert.Null(result.Player);
        Assert.Empty(result.RareAchievements);
        Assert.Equal(["character-achievements"], client.RequestedEndpoints);
    }

    [Fact]
    public async Task FetchCharacterSyncAsync_MalformedAppearanceResponse_StopsBeforeSheetRequest()
    {
        var client = new FakeTauriApiClient(
            new Dictionary<string, TauriApiResponseResult>
            {
                ["character-achievements"] = Success("""{ "Achievements": {} }"""),
                ["character-itemappearances"] = Success("""{ "itemappearances": {} }"""),
            }
        );

        var result = await FetchAsync(client);

        Assert.False(result.IsFullySuccessful);
        Assert.Null(result.Player);
        Assert.Equal(
            ["character-achievements", "character-itemappearances"],
            client.RequestedEndpoints
        );
    }

    [Fact]
    public async Task FetchCharacterSyncAsync_SheetRequestFails_DiscardsPartialPlayer()
    {
        var client = new FakeTauriApiClient(
            new Dictionary<string, TauriApiResponseResult>
            {
                ["character-achievements"] = Success("""{ "Achievements": {} }"""),
                ["character-itemappearances"] = Success(
                    """{ "itemappearances": { "owned": [] } }"""
                ),
                ["character-sheet-minimal"] = TauriApiResponseResult.Failure("Unavailable"),
            }
        );

        var result = await FetchAsync(client);

        Assert.False(result.IsFullySuccessful);
        Assert.Null(result.Player);
        Assert.Equal(3, client.RequestedEndpoints.Count);
    }

    private static Task<PlayerService.CharacterSyncResult> FetchAsync(FakeTauriApiClient client) =>
        PlayerService.FetchCharacterSyncAsync(
            client,
            "Example",
            "[EN] Evermoon",
            "Evermoon",
            ScanStartedAt,
            CancellationToken.None
        );

    private static TauriApiResponseResult Success(string json) =>
        TauriApiResponseResult.Success(JsonDocument.Parse(json).RootElement.Clone());

    private sealed class FakeTauriApiClient(
        IReadOnlyDictionary<string, TauriApiResponseResult> responses
    ) : ITauriApiClient
    {
        public List<string> RequestedEndpoints { get; } = [];

        public Task<TauriApiResponseResult> FetchResponseElementAsync(
            string endpoint,
            object parameters,
            string requestLabel,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedEndpoints.Add(endpoint);

            return Task.FromResult(
                responses.TryGetValue(endpoint, out var response)
                    ? response
                    : TauriApiResponseResult.Failure($"No fake response for {endpoint}.")
            );
        }
    }
}
