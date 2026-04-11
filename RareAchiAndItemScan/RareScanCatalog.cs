namespace RareAchiAndItemScan;

internal static class RareScanCatalog
{
    public static readonly IReadOnlyDictionary<int, string> TargetItems = new Dictionary<int, string>
    {
        { 22818, "The Plague Bearer" },
        { 23075, "Death's Bargain" },

        { 85046, "Death Knight Malevolent Elite Head" },
        { 85086, "Death Knight Malevolent Elite Shoulders" },
        { 84993, "Death Knight Malevolent Elite Chest" },
        { 73740, "Death Knight Cataclysmic Elite Head" },

        { 45983, "Furious Gladiator's Tabard" },
        { 49086, "Relentless Gladiator's Tabard" },
        { 51534, "Wrathful Gladiator's Tabard" },
        { 98162, "Tyrannical Gladiator's Tabard" },

        { 22701, "Polar Leggings" },
        { 22662, "Polar Gloves" },

        { 22691, "Corrupted Ashbringer" },
        { 18608, "Benediction" },
        { 18609, "Anathema" },
        { 18713, "Rhok'delar, Longbow of the Ancient Keepers" },
        { 18715, "Lok'delar, Stave of the Ancient Keepers" },

        { 73680, "Cataclysmic Gladiator's Leather Helm" },
        { 73679, "Cataclysmic Gladiator's Leather Legguards" },
        { 73678, "Cataclysmic Gladiator's Leather Spaulders" },
        { 73681, "Cataclysmic Gladiator's Leather Gloves" },
        { 73682, "Cataclysmic Gladiator's Leather Tunic" },
        { 65522, "Vicious Chest" },
        { 65520, "Vicious Head" },
        { 65518, "Vicious Shoulders" },
        { 65519, "Vicious Legs" },
        { 65605, "Vicious Boots" },
        { 65521, "Vicious Gloves" }
    };

    public static readonly IReadOnlyDictionary<int, string> RareAchievementNames = new Dictionary<int, string>
    {
        // Rank 1 Gladiators
        { 8666, "Prideful Gladiator" },
        { 8643, "Grievous Gladiator" },
        { 8791, "Tyrannical Gladiator" },
        { 8214, "Malevolent Gladiator" },
        { 6938, "Cataclysmic Gladiator" },
        { 6124, "Ruthless Gladiator" },
        { 6002, "Vicious Gladiator" },
        { 4599, "Wrathful Gladiator" },
        { 3758, "Relentless Gladiator" },
        { 3436, "Furious Gladiator" },
        { 3336, "Deadly Gladiator" },
        { 420,  "Brutal Gladiator" },
        { 419,  "Vengeful Gladiator" },
        { 418,  "Merciless Gladiator" },

        // Gladiator Mounts
        { 8707, "Prideful Gladiator's Cloud Serpent" },
        { 8705, "Grievous Gladiator's Cloud Serpent" },
        { 8678, "Tyrannical Gladiator's Cloud Serpent" },
        { 8216, "Malevolent Gladiator's Cloud Serpent" },
        { 6741, "Cataclysmic Gladiator's Twilight Drake" },
        { 6322, "Ruthless Gladiator's Twilight Drake" },
        { 6003, "Vicious Gladiator's Twilight Drake" },
        { 4600, "Wrathful Gladiator's Frost Wyrm" },
        { 3757, "Relentless Gladiator's Frost Wyrm" },
        { 3756, "Furious Gladiator's Frost Wyrm" },
        { 3096, "Deadly Gladiator's Frost Wyrm" },
        { 2316, "Brutal Nether Drake" },
        { 888,  "Vengeful Nether Drake" },
        { 887,  "Merciless Nether Drake" },
        { 886,  "Swift Nether Drake" },

        // Rated Battleground Horde
        { 8659, "Hero of the Horde: Prideful" },
        { 8657, "Hero of the Horde: Grievous" },
        { 8653, "Hero of the Horde: Tyrannical" },
        { 8244, "Hero of the Horde: Malevolent" },
        { 6940, "Hero of the Horde: Cataclysmic" },
        { 6317, "Hero of the Horde: Ruthless" },
        { 5358, "Hero of the Horde: Vicious" },

        // Rated Battleground Alliance
        { 8658, "Hero of the Alliance: Prideful" },
        { 8654, "Hero of the Alliance: Grievous" },
        { 8652, "Hero of the Alliance: Tyrannical" },
        { 8243, "Hero of the Alliance: Malevolent" },
        { 6939, "Hero of the Alliance: Cataclysmic" },
        { 6316, "Hero of the Alliance: Ruthless" },
        { 5344, "Hero of the Alliance: Vicious" },

        { 416,  "Scarab Lord" },
        { 425,  "Atiesh" },


        { 432,  "Champion of the Naaru" },
        { 5329, "300 RBG Wins" },
        { 6942, "Hero of the Alliance" },
        { 6941, "Hero of the Horde" },
        { 3117, "Realm First! Death's Demise" },
        { 3259, "Realm First! Celestial Defender" },
        { 6433, "Realm First! Challenge Conqueror: Gold" },
        { 1402, "Realm First! Conqueror of Naxxramas" },
        { 4576, "Realm First! Fall of the Lich King" },
        { 4078, "Realm First! Grand Crusader" },
        { 1400, "Realm First! Magic Seeker" },
        { 1463, "Realm First! Northrend Vanguard" },
        { 456,  "Realm First! Obsidian Slayer" },
        { 1415, "RF Alchemy 450" },
        { 1420, "RF Fishing 450" },
        { 5395, "RF Archeology 450" },
        { 1414, "RF Blacksmithing 450" },
        { 1416, "RF Cooking 450" },
        { 1417, "RF Enchanting 450" },
        { 1418, "RF Engineer 450" },
        { 1421, "RF Herbalism 450" },
        { 1423, "RF Jewelcrafter 450" },
        { 1424, "RF Leatherworking 450" },
        { 1419, "RF First Aid 450" },
        { 1425, "RF Mining 450" },
        { 1422, "RF Inscription 450" },
        { 1426, "RF Skinning 450" },
        { 1427, "RF Tailor 450" },
        { 457,  "RF Level 80" },
        { 1405, "RF Blood Elf 80" },
        { 461,  "RF DK 80" },
        { 1406, "RF Draenei 80" },
        { 466,  "RF Druid 80" },
        { 1407, "RF Dwarf 80" },
        { 1413, "RF Undead 80" },
        { 1404, "RF Gnome 80" },
        { 1408, "RF Human 80" },
        { 462,  "RF Hunter 80" },
        { 460,  "RF Mage 80" },
        { 1409, "RF Night Elf 80" },
        { 1410, "RF Orc 80" },
        { 465,  "RF Paladin 80" },
        { 464,  "RF Priest 80" },
        { 458,  "RF Rogue 80" },
        { 467,  "RF Shaman 80" },
        { 1411, "RF Tauren 80" },
        { 1412, "RF Troll 80" },
        { 463,  "RF Warlock 80" },
        { 459,  "RF Warrior 80" },
        { 5381, "RF Alchemy 525" },
        { 5387, "RF Fishing 525" },
        { 5396, "RF Archeology 525" },
        { 5382, "RF Blacksmithing 525" },
        { 5383, "RF Cooking 525" },
        { 5384, "RF Enchanting 525" },
        { 5385, "RF Engineer 525" },
        { 5388, "RF Herbalism 525" },
        { 5390, "RF Jewelcrafter 525" },
        { 5391, "RF Leatherworking 525" },
        { 5386, "RF First Aid 525" },
        { 5392, "RF Mining 525" },
        { 5389, "RF Inscription 525" },
        { 5393, "RF Skinning 525" },
        { 5394, "RF Tailor 525" },
        { 4999, "RF Level 85" },
        { 5005, "RF DK 85" },
        { 5000, "RF Druid 85" },
        { 5004, "RF Hunter 85" },
        { 5006, "RF Mage 85" },
        { 5001, "RF Paladin 85" },
        { 5002, "RF Priest 85" },
        { 5008, "RF Rogue 85" },
        { 4998, "RF Shaman 85" },
        { 5003, "RF Warlock 85" },
        { 5007, "RF Warrior 85" },
        { 6829, "Realm First! Pandaren Ambassador" },
        { 6859, "RF Alchemy 600" },
        { 6865, "RF Fishing 600" },
        { 6873, "RF Archeology 600" },
        { 6860, "RF Blacksmithing 600" },
        { 6861, "RF Cooking 600" },
        { 6862, "RF Enchanting 600" },
        { 6863, "RF Engineer 600" },
        { 6866, "RF Herbalism 600" },
        { 6868, "RF Jewelcrafter 600" },
        { 6869, "RF Leatherworking 600" },
        { 6864, "RF First Aid 600" },
        { 6870, "RF Mining 600" },
        { 6867, "RF Inscription 600" },
        { 6871, "RF Skinning 600" },
        { 6872, "RF Tailor 600" },
        { 6524, "RF Level 90" },
        { 6748, "RF DK 90" },
        { 6743, "RF Druid 90" },
        { 6747, "RF Hunter 90" },
        { 6749, "RF Mage 90" },
        { 6752, "RF Monk 90" },
        { 6744, "RF Paladin 90" },
        { 6745, "RF Priest 90" },
        { 6751, "RF Rogue 90" },
        { 6523, "RF Shaman 90" },
        { 6746, "RF Warlock 90" },
        { 6750, "RF Warrior 90" }
    };

    public static readonly IReadOnlySet<string> GladiatorMountNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Swift Nether Drake",
        "Merciless Nether Drake",
        "Vengeful Nether Drake",
        "Brutal Nether Drake",
        "Deadly Gladiator's Frost Wyrm",
        "Furious Gladiator's Frost Wyrm",
        "Relentless Gladiator's Frost Wyrm",
        "Wrathful Gladiator's Frost Wyrm",
        "Vicious Gladiator's Twilight Drake",
        "Ruthless Gladiator's Twilight Drake",
        "Cataclysmic Gladiator's Twilight Drake",
        "Malevolent Gladiator's Cloud Serpent",
        "Tyrannical Gladiator's Cloud Serpent",
        "Grievous Gladiator's Cloud Serpent",
        "Prideful Gladiator's Cloud Serpent",
        "Primal Gladiator's Felblood Gronnling",
        "Wild Gladiator's Felblood Gronnling",
        "Warmongering Gladiator's Felblood Gronnling",
        "Vindictive Gladiator's Storm Dragon",
        "Fearless Gladiator's Storm Dragon",
        "Cruel Gladiator's Storm Dragon",
        "Ferocious Gladiator's Storm Dragon",
        "Fierce Gladiator's Storm Dragon",
        "Dominant Gladiator's Storm Dragon",
        "Demonic Gladiator's Storm Dragon"
    };

    private static readonly IReadOnlyDictionary<int, string> ClassNames = new Dictionary<int, string>
    {
        { 1, "Warrior" },
        { 2, "Paladin" },
        { 3, "Hunter" },
        { 4, "Rogue" },
        { 5, "Priest" },
        { 6, "Death Knight" },
        { 7, "Shaman" },
        { 8, "Mage" },
        { 9, "Warlock" },
        { 10, "Monk" },
        { 11, "Druid" },
        { 12, "Demon Hunter" }
    };

    public static string ClassNameFromId(int classId)
    {
        if (ClassNames.TryGetValue(classId, out var className))
        {
            return className;
        }

        return classId > 0 ? $"Class {classId}" : "Unknown";
    }

    public static string ItemNameFromId(int itemId)
    {
        if (TargetItems.TryGetValue(itemId, out var itemName))
        {
            return itemName;
        }

        return $"Item {itemId}";
    }
}
