using System.Text.Json;
using BattlegroundCollector;
using Tauri.Core.Infrastructure;

namespace AchievementLadder.Tests;

public sealed class BattlegroundCollectorServiceTests
{
    [Fact]
    public async Task ExecuteAsync_AppendsOnlyNewRankedFullResponses()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            $"battleground-collector-tests-{Guid.NewGuid():N}"
        );
        var projectRoot = Path.Combine(testRoot, "BattlegroundCollector");
        var frontendSrc = Path.Combine(testRoot, "frontend", "src");
        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(frontendSrc);

        try
        {
            var ratedOutputPath = Path.Combine(frontendSrc, "rated-battlegrounds.json");
            await File.WriteAllTextAsync(
                ratedOutputPath,
                """
                [
                  {
                    "matchid": 100,
                    "isranked": true,
                    "sentinel": "existing"
                  }
                ]
                """
            );

            var apiClient = new MatchSequenceApiClient(
                new Dictionary<int, TauriApiResponseResult>
                {
                    [100] = Success(MatchJson(100, "Warsong Gulch", isRanked: true, "duplicate")),
                    [101] = Success(MatchJson(101, "Warsong Gulch", isRanked: false, "unranked")),
                    [102] = Success(MatchJson(102, "Warsong Gulch", isRanked: true, "complete-response")),
                    [103] = Success(MatchJson(103, "Blade's Edge Arena", isRanked: true, "rated-arena")),
                }
            );
            var collector = new BattlegroundCollectorService(
                projectRoot,
                testRoot,
                frontendSrc,
                apiClient
            );
            var options = new BattlegroundCollectorOptions(
                100,
                "[EN] Evermoon",
                "Evermoon",
                null,
                null
            );

            var result = await collector.ExecuteAsync(options, CancellationToken.None);

            Assert.Equal(1, result.NewRatedBattlegroundCount);
            Assert.Equal(2, result.TotalRatedBattlegroundCount);
            Assert.Equal(ratedOutputPath, result.RatedOutputPath);

            using var output = JsonDocument.Parse(await File.ReadAllTextAsync(ratedOutputPath));
            var matches = output.RootElement.EnumerateArray().ToList();
            Assert.Equal([100, 102], matches.Select(match => match.GetProperty("matchid").GetInt32()));
            Assert.Equal("existing", matches[0].GetProperty("sentinel").GetString());
            Assert.Equal(
                "complete-response",
                matches[1].GetProperty("sentinel").GetString()
            );
            Assert.True(matches[1].GetProperty("nested").GetProperty("preserved").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    private static string MatchJson(
        int matchId,
        string mapName,
        bool isRanked,
        string sentinel
    ) =>
        $$"""
        {
          "matchid": {{matchId}},
          "mapname": "{{mapName}}",
          "starttime": 1789700000,
          "length": 600000,
          "isranked": {{isRanked.ToString().ToLowerInvariant()}},
          "sentinel": "{{sentinel}}",
          "nested": { "preserved": true },
          "members": []
        }
        """;

    private static TauriApiResponseResult Success(string json) =>
        TauriApiResponseResult.Success(JsonDocument.Parse(json).RootElement.Clone());

    private sealed class MatchSequenceApiClient(
        IReadOnlyDictionary<int, TauriApiResponseResult> responses
    ) : ITauriApiClient
    {
        public Task<TauriApiResponseResult> FetchResponseElementAsync(
            string endpoint,
            object parameters,
            string requestLabel,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("pvp-match", endpoint);

            var parametersJson = JsonSerializer.SerializeToElement(parameters);
            var matchId = int.Parse(parametersJson.GetProperty("matchid").GetString()!);
            return Task.FromResult(
                responses.TryGetValue(matchId, out var response)
                    ? response
                    : TauriApiResponseResult.Failure("End of fake match sequence.")
            );
        }
    }
}
