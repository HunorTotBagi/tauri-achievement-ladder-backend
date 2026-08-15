using System.Text.Json;

namespace Tauri.Core.Infrastructure;

public static class CharacterItemLevelCalculator
{
    public static decimal? Calculate(JsonElement response)
    {
        var equippedItems =
            response.TryGetProperty("characterItems", out var characterItems)
            && characterItems.ValueKind == JsonValueKind.Array
                ? characterItems
                    .EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Object)
                    .Select(item => new EquippedItem(
                        ReadInt(item, "InventoryType"),
                        NormalizeItemLevel(ReadInt(item, "ilevel")),
                        ReadInt(item, "rarity"),
                        item.TryGetProperty("artifact", out var artifact)
                            && artifact.ValueKind == JsonValueKind.Object
                    ))
                    .Where(item => IsCombatEquipment(item.InventoryType) && item.ItemLevel > 0)
                    .ToList()
                : [];

        if (equippedItems.Count == 0)
        {
            return null;
        }

        var mainHand = equippedItems.FirstOrDefault(item =>
            item.InventoryType is 13 or 15 or 17 or 21 or 25 or 26
        );
        var offHandIndex = equippedItems.FindIndex(item => item.InventoryType is 14 or 22 or 23);

        if (
            mainHand is not null
            && mainHand.IsArtifact
            && offHandIndex >= 0
            && equippedItems[offHandIndex] is { ItemLevel: 750, Rarity: 6 }
        )
        {
            equippedItems[offHandIndex] = equippedItems[offHandIndex] with
            {
                ItemLevel = mainHand.ItemLevel,
            };
        }

        var itemLevelTotal = equippedItems.Sum(item => item.ItemLevel);
        var itemCount = equippedItems.Count;
        if (mainHand is not null && mainHand.InventoryType is 17 or 26 && offHandIndex < 0)
        {
            itemLevelTotal += mainHand.ItemLevel;
            itemCount++;
        }

        return Math.Round(
            (decimal)itemLevelTotal / itemCount,
            2,
            MidpointRounding.AwayFromZero
        );
    }

    private static int NormalizeItemLevel(int itemLevel) => itemLevel == 910 ? 895 : itemLevel;

    private static bool IsCombatEquipment(int inventoryType) =>
        inventoryType
            is 1
                or 2
                or 3
                or 5
                or 6
                or 7
                or 8
                or 9
                or 10
                or 11
                or 12
                or 13
                or 14
                or 15
                or 16
                or 17
                or 21
                or 22
                or 23
                or 25
                or 26;

    private static int ReadInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var number) => number,
            _ => 0,
        };
    }

    private sealed record EquippedItem(
        int InventoryType,
        int ItemLevel,
        int Rarity,
        bool IsArtifact
    );
}
