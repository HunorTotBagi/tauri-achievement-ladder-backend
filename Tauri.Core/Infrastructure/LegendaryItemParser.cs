using System.Text.Json;
using System.Xml.Linq;

namespace Tauri.Core.Infrastructure;

public static class LegendaryItemParser
{
    private const string IconBaseUrl = "https://legion-static.tauri.hu/images/icons/large";

    public static IReadOnlyList<LegendaryItem> ReadEquipped(JsonElement characterSheet)
    {
        if (
            !characterSheet.TryGetProperty("characterItems", out var characterItems)
            || characterItems.ValueKind != JsonValueKind.Array
        )
        {
            return [];
        }

        return characterItems
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object && ReadInt(item, "rarity") == 5)
            .Select(item => new LegendaryItem(
                ReadInt(item, "entry"),
                ReadString(item, "name", "originalname"),
                MakeIconUrl(ReadString(item, "icon", "originalicon")),
                null
            ))
            .Where(item => item.Id > 0)
            .GroupBy(item => item.Id)
            .Select(group => group.First())
            .ToList();
    }

    public static LegendaryItem ParseTooltipXml(
        string xml,
        LegendaryItem fallback
    )
    {
        var item = XDocument.Parse(xml).Root?.Element("item");
        if (item is null)
        {
            return fallback;
        }

        var id = int.TryParse(item.Attribute("id")?.Value, out var parsedId)
            ? parsedId
            : fallback.Id;
        var name = NonEmpty(item.Element("name")?.Value, fallback.Name);
        var iconName = item.Element("icon")?.Value;
        var icon = string.IsNullOrWhiteSpace(iconName)
            ? fallback.Icon
            : MakeIconUrl(iconName);
        var tooltipHtml = item.Element("htmlTooltip")?.Value.Trim();

        return new LegendaryItem(
            id,
            name,
            icon,
            string.IsNullOrWhiteSpace(tooltipHtml) ? null : tooltipHtml
        );
    }

    private static string MakeIconUrl(string icon)
    {
        var iconName = Path.GetFileNameWithoutExtension(icon.Trim()).ToLowerInvariant();
        return string.IsNullOrWhiteSpace(iconName)
            ? string.Empty
            : $"{IconBaseUrl}/{Uri.EscapeDataString(iconName)}.png";
    }

    private static string NonEmpty(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static int ReadInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
        && property.TryGetInt32(out var value)
            ? value
            : 0;

    private static string ReadString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (
                element.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(property.GetString())
            )
            {
                return property.GetString()!.Trim();
            }
        }

        return string.Empty;
    }
}

public sealed record LegendaryItem(int Id, string Name, string Icon, string? TooltipHtml);
