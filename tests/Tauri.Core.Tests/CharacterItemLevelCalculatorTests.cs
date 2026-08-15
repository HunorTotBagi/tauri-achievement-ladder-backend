using System.Text.Json;
using Tauri.Core.Infrastructure;

namespace Tauri.Core.Tests;

public sealed class CharacterItemLevelCalculatorTests
{
    [Fact]
    public void Calculate_ArtifactWithPlaceholderOffHand_UsesMainHandLevelForBothWeapons()
    {
        var response = Parse(
            """
            {
              "characterItems": [
                { "InventoryType": 1, "ilevel": 850, "rarity": 4 },
                { "InventoryType": 21, "ilevel": 900, "rarity": 6, "artifact": {} },
                { "InventoryType": 22, "ilevel": 750, "rarity": 6 }
              ]
            }
            """
        );

        Assert.Equal(883.33m, CharacterItemLevelCalculator.Calculate(response));
    }

    [Fact]
    public void Calculate_TwoHandedWeaponWithoutOffHand_CountsWeaponTwiceAndNormalizes910()
    {
        var response = Parse(
            """
            {
              "characterItems": [
                { "InventoryType": 1, "ilevel": 910, "rarity": 4 },
                { "InventoryType": 17, "ilevel": 900, "rarity": 4 }
              ]
            }
            """
        );

        Assert.Equal(898.33m, CharacterItemLevelCalculator.Calculate(response));
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
