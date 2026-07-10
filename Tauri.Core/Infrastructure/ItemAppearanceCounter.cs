using System.Text.Json;

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
}
