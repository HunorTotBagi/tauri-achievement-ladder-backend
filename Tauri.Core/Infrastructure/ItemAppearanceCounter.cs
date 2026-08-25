using System.Text.Json;
using Tauri.Core.Models;

namespace Tauri.Core.Infrastructure;

public static class ItemAppearanceCounter
{
    public static bool TryCountOwned(JsonElement response, out int appearanceCount)
    {
        appearanceCount = 0;

        if (
            !response.TryGetProperty("itemappearances", out var itemAppearances)
            || !itemAppearances.TryGetProperty("owned", out var owned)
            || owned.ValueKind != JsonValueKind.Array
        )
        {
            return false;
        }

        foreach (var itemTypeGroup in owned.EnumerateArray())
        {
            if (itemTypeGroup.ValueKind == JsonValueKind.Array)
            {
                appearanceCount += itemTypeGroup.GetArrayLength();
            }
        }

        return true;
    }

    public static bool TryFindOwned(
        JsonElement response,
        IReadOnlyDictionary<int, RareItemDefinition> targetItems,
        out IReadOnlyList<RareItemDefinition> foundItems
    )
    {
        foundItems = Array.Empty<RareItemDefinition>();

        if (
            !response.TryGetProperty("itemappearances", out var itemAppearances)
            || !itemAppearances.TryGetProperty("owned", out var owned)
            || owned.ValueKind != JsonValueKind.Array
        )
        {
            return false;
        }

        var foundIds = new HashSet<int>();
        foreach (var itemTypeGroup in owned.EnumerateArray())
        {
            if (itemTypeGroup.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var itemIdElement in itemTypeGroup.EnumerateArray())
            {
                if (itemIdElement.TryGetInt32(out var itemId) && targetItems.ContainsKey(itemId))
                {
                    foundIds.Add(itemId);
                }
            }
        }

        foundItems = foundIds
            .Select(id => targetItems[id])
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Id)
            .ToList();
        return true;
    }
}
