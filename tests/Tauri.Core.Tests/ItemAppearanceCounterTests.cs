using System.Text.Json;
using Tauri.Core.Infrastructure;
using Tauri.Core.Models;

namespace Tauri.Core.Tests;

public sealed class ItemAppearanceCounterTests
{
    [Fact]
    public void TryFindOwned_ReturnsConfiguredItemsOnlyOnce()
    {
        var response = Parse(
            """{ "itemappearances": { "owned": [[22818, 7], [23075, 22818]] } }"""
        );
        var targets = new Dictionary<int, RareItemDefinition>
        {
            [22818] = new(22818, "The Plague Bearer"),
            [23075] = new(23075, "Death's Bargain"),
            [22691] = new(22691, "Corrupted Ashbringer"),
        };

        var succeeded = ItemAppearanceCounter.TryFindOwned(response, targets, out var found);

        Assert.True(succeeded);
        Assert.Equal([23075, 22818], found.Select(item => item.Id));
    }

    [Fact]
    public void TryFindOwned_ObjectEntries_ReadsKnownItemIdShapesWithoutThrowing()
    {
        var response = Parse(
            """
            {
              "itemappearances": {
                "owned": [[
                  { "id": 22818, "quality": 5 },
                  { "itemId": 23075 },
                  { "22691": true },
                  { "unexpected": 85046 }
                ]]
              }
            }
            """
        );
        var targets = new Dictionary<int, RareItemDefinition>
        {
            [22818] = new(22818, "The Plague Bearer"),
            [23075] = new(23075, "Death's Bargain"),
            [22691] = new(22691, "Corrupted Ashbringer"),
            [85046] = new(85046, "DK Malev elite head"),
        };

        var succeeded = ItemAppearanceCounter.TryFindOwned(response, targets, out var found);

        Assert.True(succeeded);
        Assert.Equal([22691, 23075, 22818], found.Select(item => item.Id));
    }

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
