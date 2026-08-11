using System.Text.Json;
using Tauri.Core.Infrastructure;

namespace Tauri.Core.Tests;

public sealed class ItemAppearanceCounterTests
{
    [Fact]
    public void TryCountOwned_MultipleGroups_ReturnsTotalAppearanceCount()
    {
        var response = Parse("""{ "itemappearances": { "owned": [[1, 2], [3], []] } }""");

        var succeeded = ItemAppearanceCounter.TryCountOwned(response, out var count);

        Assert.True(succeeded);
        Assert.Equal(3, count);
    }

    [Fact]
    public void TryCountOwned_NonArrayGroups_IgnoresInvalidGroups()
    {
        var response = Parse(
            """{ "itemappearances": { "owned": [[1], null, "invalid", { "id": 2 }] } }"""
        );

        var succeeded = ItemAppearanceCounter.TryCountOwned(response, out var count);

        Assert.True(succeeded);
        Assert.Equal(1, count);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{ \"itemappearances\": {} }")]
    [InlineData("{ \"itemappearances\": { \"owned\": null } }")]
    public void TryCountOwned_MissingOrInvalidOwnedArray_ReturnsFailure(string json)
    {
        var succeeded = ItemAppearanceCounter.TryCountOwned(Parse(json), out var count);

        Assert.False(succeeded);
        Assert.Equal(0, count);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
