using System.Text.Json;
using Tauri.Core.Infrastructure;

namespace Tauri.Core.Tests;

public sealed class LegendaryItemParserTests
{
    [Fact]
    public void ReadEquipped_ReturnsOnlyEquippedLegendaryItems()
    {
        var sheet = Parse(
            """
            {
              "characterItems": [
                { "entry": 132452, "rarity": 5, "name": "Sephuz's Secret", "icon": "inv_jewelry_ring_149" },
                { "entry": 128935, "rarity": 6, "name": "The Fist of Ra-den", "icon": "inv_mace_1h_artifactazshara_d_04" },
                { "entry": 139219, "rarity": 4, "name": "Other item", "icon": "inv_misc_questionmark" }
              ]
            }
            """
        );

        var item = Assert.Single(LegendaryItemParser.ReadEquipped(sheet));
        Assert.Equal(132452, item.Id);
        Assert.Equal("Sephuz's Secret", item.Name);
        Assert.Equal(
            "https://legion-static.tauri.hu/images/icons/large/inv_jewelry_ring_149.png",
            item.Icon
        );
        Assert.Null(item.TooltipHtml);
    }

    [Fact]
    public void ParseTooltipXml_UsesShootMetadataAndTooltip()
    {
        var fallback = new LegendaryItem(137033, "Fallback", "fallback.png", null);
        var result = LegendaryItemParser.ParseTooltipXml(
            """
            <wowhead><item id="137033"><name><![CDATA[Ullr's Feather Snowshoes]]></name><icon>inv_boots_mail_08</icon><htmlTooltip><![CDATA[<b class="q5">Ullr's Feather Snowshoes</b>]]></htmlTooltip></item></wowhead>
            """,
            fallback
        );

        Assert.Equal("Ullr's Feather Snowshoes", result.Name);
        Assert.Equal(
            "https://legion-static.tauri.hu/images/icons/large/inv_boots_mail_08.png",
            result.Icon
        );
        Assert.Equal("<b class=\"q5\">Ullr's Feather Snowshoes</b>", result.TooltipHtml);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
