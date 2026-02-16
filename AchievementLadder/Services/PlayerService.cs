using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using AchievementLadder.Dtos;
using AchievementLadder.Helpers;
using AchievementLadder.Models;
using AchievementLadder.Repositories;

namespace AchievementLadder.Services;

public class PlayerService(IPlayerRepository playerRepository, IWebHostEnvironment webHostEnvironment, PlayerCsvStore csvStore) : IPlayerService
{
    public async Task SyncData(CancellationToken cancellationToken)
    {
        var projectRoot = webHostEnvironment.ContentRootPath;
        using var client = new HttpClient();

        var config = new ConfigurationBuilder()
            .SetBasePath(projectRoot)
            .AddJsonFile("appsettings.Development.json", optional: false, reloadOnChange: true)
            .Build();

        string baseUrl = config["TauriApi:BaseUrl"] ?? string.Empty;
        string apiKey = config["TauriApi:ApiKey"] ?? string.Empty;
        string secret = config["TauriApi:Secret"] ?? string.Empty;

        string apiUrl = $"{baseUrl}?apikey={apiKey}";

        var allCharacters = new List<(string Name, string ApiRealm, string DisplayRealm)>();

        CharacterHelpers.LoadCharacters(projectRoot, "evermoon-achi.txt", "[EN] Evermoon", "Evermoon", allCharacters);
        CharacterHelpers.LoadCharacters(projectRoot, "evermoon-hk.txt", "[EN] Evermoon", "Evermoon", allCharacters);
        CharacterHelpers.LoadCharacters(projectRoot, "evermoon-playTime.txt", "[EN] Evermoon", "Evermoon", allCharacters);

        CharacterHelpers.LoadCharacters(projectRoot, "tauri-achi.txt", "[HU] Tauri WoW Server", "Tauri", allCharacters);
        CharacterHelpers.LoadCharacters(projectRoot, "tauri-hk.txt", "[HU] Tauri WoW Server", "Tauri", allCharacters);
        CharacterHelpers.LoadCharacters(projectRoot, "tauri-playTime.txt", "[HU] Tauri WoW Server", "Tauri", allCharacters);

        CharacterHelpers.LoadCharacters(projectRoot, "wod-achi.txt", "[HU] Warriors of Darkness", "WoD", allCharacters);
        CharacterHelpers.LoadCharacters(projectRoot, "wod-hk.txt", "[HU] Warriors of Darkness", "WoD", allCharacters);
        CharacterHelpers.LoadCharacters(projectRoot, "wod-playTime.txt", "[HU] Warriors of Darkness", "WoD", allCharacters);

        var targetGuilds = new[]
        {
            new { GuildName = "Competence Optional", RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Skill Issue",         RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Despair",             RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Mythic",              RealmApi = "[HU] Warriors of Darkness", RealmDisplay = "WoD" },
            new { GuildName = "Cara Máxima",         RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Outlaws",             RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Guild of Multiverse", RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Defiance",            RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Thunder",             RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Army of Divergent",   RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Infernum",            RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Vistustan",           RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Yin Yang",            RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Impact",              RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Shadow Hunters",      RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Fekete Hold",         RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Last Whisper",        RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Punishers",           RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Искатели легенд",     RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },  

            // Random guilds
            new { GuildName = "Wipes on Tresh",      RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "beloved",             RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "BOOSTED",             RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Insane",              RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Killepitsch",         RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Public enemies",      RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Lighting Darkness",   RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Detoxin",             RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Zugfözde",            RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Death Vengeance",     RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Dawnguard",           RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Çonquerors",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Csak semmi CICÓ",     RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Darkey Mort",         RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "DevilsArmy",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Sinn Féin",           RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Fire Falcon",         RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Destroyer",           RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Sapped girls cant say no",   RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Resurrection",        RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Angel Rune",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Beton Logok Szar Karikkal",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Logic",               RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Välhalla",            RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Ace Of Spades",       RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Ace Of Spadez",       RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Solo Leveling",       RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Last Try",            RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Rycerze Ortalionu",   RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Storm",               RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Endless",             RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "CannabisCountryClub", RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Calamity",            RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Unicorns",            RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Crows at Midnight",   RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "The World Eaters",    RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Redemption",          RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Aurora",              RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Rosszcsontok",        RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "The Dark Exile",      RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "The Glory Avengers",  RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Army of Divergent",   RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Nexxus",              RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Ex Nihilo",           RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Leviathan",           RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Хранители Вечности",  RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Best Random Group",   RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Brodate Drzewa",      RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Resolve",             RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Mythic Regression",   RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Test Drive",          RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Diamond Dogs",        RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Pack of Lone Wolves", RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },

            // Random Random Guilds
            new { GuildName = "HiveFive",                RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Siege",                   RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "OID MORTALES",            RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Loktar Ogar",             RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Wanderers of Azeroth",    RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Chaos Inc",               RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "La Realeza",              RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Czechoslovakia",          RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Warlords of Destruction", RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "A Murder of Crows",       RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "ForTheAlliance",          RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "The Echoes",              RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Hungarian Army",          RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Dark Forest",             RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Grupo Tera",              RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Tribunes of the Light",   RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Seekers of the Self",     RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Unbound",                 RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Coven of Capybaras",      RealmApi = "[HU] Warriors of Darkness", RealmDisplay = "WoD" },

            // Old guilds from Tauri Progress
            new { GuildName = "Bad Choice",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Dark Synergy",        RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Conclusion",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Reunited",            RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Pandemonium",         RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Crøwd Cøntrøll",      RealmApi = "[HU] Warriors of Darkness", RealmDisplay = "WoD" },
            new { GuildName = "Forgotten Society",   RealmApi = "[HU] Warriors of Darkness", RealmDisplay = "WoD" },
            new { GuildName = "Kawaii Pandas",       RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Muzykanci z Gruzji",  RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },

            // Guilds from Zero
            new { GuildName = "Flare of the Heavens",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Chaos Theory",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Champion of Alliance",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Destroyer",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "KELL A RÉZ A TELÓDBÓL",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Ganksters Paradise",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Serenity",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "The Guardians",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Lila Orhideák",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "HolidayRaiders",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Seekers of the Self",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Çonquerors",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Shifty Rascals",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Arcanum",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Apothecary",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Durability",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Cafe of the old man",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Best of The Best",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Hordecore Pørnstars",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "The Redeemers",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Nonsense",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "High and Mighty",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Százas Zsepi",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Apothecary",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Alpárcty",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Elders of illuminat",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Thános Gyermekei",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Revision",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Tauri All Stars",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Darkmist Vortex",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Szörny RT",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Dabrecen",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "NothingPersonal",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Conclusion",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "MyTour",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Titans",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Furious Angels",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Legends Never Die",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Szeran",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "LOOKING 4 GROUP",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Wu Tang",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "MyTour",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "In Memoriam Estranged",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "DevilsArmy",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "TOTAL ANNIHILATION",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Sapped girls cant say no",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Tropic Thunder",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Killepitsch",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "The Crusade",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Remains of Nomen",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "All In",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Evil Knights",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Last Whisper",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Renegade",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Hármas raktár",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Angel Rune",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Darkmist Vortex",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Inspecteldafaszom",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Chaos Destroyers",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Titkos Mikkentyűk",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "The Royal Within",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "International",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Punisher",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Arcaneum",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Beton Logok Szar Karikkal",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Forgotten Society",   RealmApi = "[HU] Warriors of Darkness", RealmDisplay = "WoD" },
            new { GuildName = "Inquisition",   RealmApi = "[HU] Warriors of Darkness", RealmDisplay = "WoD" },
            new { GuildName = "S.W.R.C",   RealmApi = "[HU] Warriors of Darkness", RealmDisplay = "WoD" },
            new { GuildName = "Mythic",   RealmApi = "[HU] Warriors of Darkness", RealmDisplay = "WoD" },

            // Feb-14-New Guilds
            new { GuildName = "Purple Stuff",       RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "TeamWork",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Vén Kecskék",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Guild of Multiverse",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "A Galaxis Örzöi",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Aljanépség",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Angels of Faith",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Angels of the Phoenix",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Behemots Division",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "BOOSTED",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Last Whisper",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Infernum",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Zugfözde",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Excluded",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Lighting Darkness",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Die Hard",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Lonely Wanderers",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Never Ending Nightmare",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Low Fast Fox",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Death Vengeance",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "The Dynasty",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Eternal",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Recharge",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Azeroth Zsoldosai",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Echo of Silence",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Polgár Jenõ",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Dragon's Dogma",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Order Of The Warriors",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Darkey Mort",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Eye of the Phoenix",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Corpsegrind Pálinkái",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Holy Sentinels",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Commodore Sîxty Four",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "The Untouchables",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Idosek Klubja",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Hordecore Pørnstars",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Angel Rune",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Immortal",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Kárhozottak",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "The Big Bang Theory",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Nightmare",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Falka",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "patient",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "CENZORED",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "The Unforgiven",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Victorious Secret",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Death Vengeance",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "The Predators",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Resurrection",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Rémálom Az Elf Utcában",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Exemplar",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "The Executioners Of Sargeras",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "HATERS ARE NOT WELCOME",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Ex Mortis",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "ernourasag zsoldosai",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Vanquish",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "StudentsOfDeatH",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Woodland Critters",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Renegade",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "HalálSzárny",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Live for the Kill",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Soldiers of Moon",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Demons Of Decay",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Talicska",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Fire Falcon",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "THUG LIFE",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Cross",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Fallen Angels",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Moonlight Warriors",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Hóban Táncolók",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Sangye Choeling",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "hordlake",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Eternal Determination",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Chaos Destroyers",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Legion of Iron",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Left Over",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Shifty Rascals",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Syndicate of Resistance",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Pandora",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Barbár Vihar",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "DisillusiØned",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Detoxin",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Darkmist Vortex",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Cradle",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "RelaxBroImPro",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Spellbook Clickers",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Titans",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "The Redeemers",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Do Violence",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Bird Keeper Company",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Mindhalálig Rock and WoW",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "The Shadow Alliance",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Divine Brotherhood",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Ganksters Paradise",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Conclusion",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Serenity",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Dark Room",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Tropic Thunder",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Sinn Féin",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Inner Sanctum",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Shadows",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Exemplar",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Tauri Kedvencei",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "MacskaSzem",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Yakitori",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Toxic Souls",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Reborn of Aegis",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "The Light of Dawn",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "GerillaOsztag",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Not in Guild",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Son of Anarchy",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "oO AFK Oo",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Deep Inside",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Council of the Phantom",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "TOTAL ANNIHILATION",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Bunkók és Parasztok",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Pure Dominion",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "DragonWolves",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "PØWER",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Mithico Cosa Nostra",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Echoes",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Cross",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Panda Love",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Xcution",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Second Chance",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "You GoT TutOWND",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Defenders of Nagrand",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Vrye Gees",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Sonka Osztag",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Absolute Loyalty",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Hammerite",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "The Special Edition",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "DIVINlTY",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Orgrimmar Police Officer",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "HURKARÁGÓK",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Bloodforge",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "The Invictus",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Leviathan",       RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Purple Stuff",       RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Valor Lovers",       RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
        };

        foreach (var g in targetGuilds)
        {
            await CharacterHelpers.LoadGuildMembersLevel90Async(
                g.GuildName,
                g.RealmApi,
                g.RealmDisplay,
                apiUrl,
                secret,
                allCharacters,
                client);
        }

        var distinctCharacters = allCharacters
            .Where(x => !string.IsNullOrWhiteSpace(x.Name) && !x.Name.Contains('#'))
            .Distinct()
            .ToList();

        var cetZone = TimeZoneInfo.FindSystemTimeZoneById(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "Central European Standard Time" 
                : "Europe/Belgrade"
        );

        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, cetZone);

        var players = new List<Player>(distinctCharacters.Count);

        const int maxConcurrency = 20;
        using var throttler = new SemaphoreSlim(maxConcurrency);

        int done = 0;

        var tasks = distinctCharacters.Select(async ch =>
        {
            await throttler.WaitAsync(cancellationToken);
            try
            {
                var p = await FetchPlayerAsync(
                    client, apiUrl, secret,
                    ch.Name, ch.ApiRealm, ch.DisplayRealm,
                    today,
                    cancellationToken);

                if (p is not null)
                {
                    lock (players) players.Add(p);
                }

                var current = Interlocked.Increment(ref done);
                if (current % 250 == 0)
                {
                    Console.WriteLine($"Fetched {current}/{distinctCharacters.Count}");
                }
            }
            finally
            {
                throttler.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        await csvStore.WriteAsync(players, "Players.csv", cancellationToken);
        await csvStore.WriteLastUpdatedAsync(today, cancellationToken);
    }

    private static async Task<Player?> FetchPlayerAsync(
    HttpClient client,
    string apiUrl,
    string secret,
    string name,
    string apiRealm,
    string displayRealm,
    DateTime todayUtc,
    CancellationToken ct)
    {
        var body = new
        {
            secret,
            url = "character-sheet",
            @params = new { r = apiRealm, n = name }
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync(apiUrl, content, ct);
        if (!response.IsSuccessStatusCode) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var root = doc.RootElement;
        if (!root.TryGetProperty("response", out var resp)) return null;

        int race = resp.TryGetProperty("race", out var v) ? v.GetInt32() : 0;
        int gender = resp.TryGetProperty("gender", out v) ? v.GetInt32() : 0;
        int @class = resp.TryGetProperty("class", out v) ? v.GetInt32() : 0;
        int pts = resp.TryGetProperty("pts", out v) ? v.GetInt32() : 0;
        int hk = resp.TryGetProperty("playerHonorKills", out v) ? v.GetInt32() : 0;
        string faction = resp.TryGetProperty("faction_string_class", out v) ? (v.GetString() ?? "") : "";
        string guild = resp.TryGetProperty("guildName", out v) ? (v.GetString() ?? "") : "";
        int mountCount = await FetchMountCountAsync(client, apiUrl, secret, name, apiRealm, ct);

        return new Player
        {
            Name = name,
            Race = race,
            Gender = gender,
            Class = @class,
            Realm = displayRealm,
            Guild = guild,
            AchievementPoints = pts,
            HonorableKills = hk,
            Faction = faction,
            LastUpdated = todayUtc,
            MountCount = mountCount
        };
    }

    public async Task<IReadOnlyList<LadderEntryDto>> GetSortedByAchievements(int pageNumber, int pageSize, string? realm = null, string? faction = null, int? playerClass = null, CancellationToken cancellationToken = default)
    {
        pageNumber = pageNumber < 1 ? 1 : pageNumber;
        pageSize = pageSize is < 1 or > 500 ? 100 : pageSize;

        var skip = (pageNumber - 1) * pageSize;

        var players = await playerRepository.GetSortedByAchievements(pageSize, skip, realm, faction, playerClass, cancellationToken);

        return players.Select(p => new LadderEntryDto(
            p.Name,
            p.Race,
            p.Gender,
            p.Class,
            p.Realm,
            p.Guild ?? string.Empty,
            p.AchievementPoints,
            p.HonorableKills,
            p.Faction ?? string.Empty
        )).ToList();
    }

    public async Task<IReadOnlyList<LadderEntryDto>> GetSortedByHonorableKills(int pageNumber, int pageSize, string? realm = null, string? faction = null, int? playerClass = null, CancellationToken cancellationToken = default)
    {
        pageNumber = pageNumber < 1 ? 1 : pageNumber;
        pageSize = pageSize is < 1 or > 500 ? 100 : pageSize;

        var skip = (pageNumber - 1) * pageSize;

        var players = await playerRepository.GetSortedByHonorableKills(pageSize, skip, realm, faction, playerClass, cancellationToken);

        return players.Select(p => new LadderEntryDto(
            p.Name,
            p.Race,
            p.Gender,
            p.Class,
            p.Realm,
            p.Guild ?? string.Empty,
            p.AchievementPoints,
            p.HonorableKills,
            p.Faction ?? string.Empty
        )).ToList();
    }

    private static async Task<int> FetchMountCountAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        string name,
        string apiRealm,
        CancellationToken ct)
    {
        var body = new
        {
            secret,
            url = "character-mounts",
            @params = new { r = apiRealm, n = name }
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync(apiUrl, content, ct);
        if (!response.IsSuccessStatusCode) return 0;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("response", out var resp)) return 0;

        // mounts is at response.mounts
        if (!resp.TryGetProperty("mounts", out var mountsEl)) return 0;
        if (mountsEl.ValueKind != JsonValueKind.Array) return 0;

        return mountsEl.GetArrayLength();
    }

}
