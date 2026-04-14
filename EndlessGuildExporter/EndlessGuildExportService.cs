using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using AchievementLadder.Configuration;

namespace EndlessGuildExporter;

public sealed class EndlessGuildExportService(string solutionRoot, TauriApiOptions apiOptions)
{
    private const string TargetGuildName = "Endless";
    private const string TargetApiRealm = "[EN] Evermoon";
    private const string TargetDisplayRealm = "Evermoon";
    private const string DefaultExportFileName = "Endless-Legion-Roster.xlsx";
    private const string HeaderStyleKey = "Header";
    private const string CheckedSymbol = "☑";
    private const string UncheckedSymbol = "☐";
    private const int MaxDegreeOfParallelism = 8;

    private static readonly string[] DirectProfessionPropertyNames =
    [
        "prof1",
        "prof2",
        "proff1",
        "proff2",
        "profession1",
        "profession2",
        "profession_1",
        "profession_2",
        "prof_1",
        "prof_2",
        "primaryProfession1",
        "primaryProfession2"
    ];

    private static readonly HashSet<string> PrimaryProfessionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Alchemy",
        "Blacksmithing",
        "Enchanting",
        "Engineering",
        "Herbalism",
        "Inscription",
        "Jewelcrafting",
        "Leatherworking",
        "Mining",
        "Skinning",
        "Tailoring"
    };

    private static readonly string[] KnownProfessionNames =
    [
        "Alchemy",
        "Archaeology",
        "Blacksmithing",
        "Cooking",
        "Enchanting",
        "Engineering",
        "First Aid",
        "Fishing",
        "Herbalism",
        "Inscription",
        "Jewelcrafting",
        "Leatherworking",
        "Mining",
        "Skinning",
        "Tailoring"
    ];

    private static readonly IReadOnlyDictionary<int, string> ClassNames = new Dictionary<int, string>
    {
        [1] = "Warrior",
        [2] = "Paladin",
        [3] = "Hunter",
        [4] = "Rogue",
        [5] = "Priest",
        [6] = "Death Knight",
        [7] = "Shaman",
        [8] = "Mage",
        [9] = "Warlock",
        [10] = "Monk",
        [11] = "Druid",
        [12] = "Demon Hunter",
        [13] = "Evoker"
    };

    private static readonly IReadOnlyDictionary<string, string> ClassColorHexByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Death Knight"] = "C41E3A",
        ["Demon Hunter"] = "A330C9",
        ["Druid"] = "FF7C0A",
        ["Evoker"] = "33937F",
        ["Hunter"] = "AAD372",
        ["Mage"] = "3FC7EB",
        ["Monk"] = "00FF98",
        ["Paladin"] = "F48CBA",
        ["Priest"] = "FFFFFF",
        ["Rogue"] = "FFF468",
        ["Shaman"] = "0070DD",
        ["Warlock"] = "8788EE",
        ["Warrior"] = "C69B6D"
    };

    private static readonly IReadOnlyDictionary<int, string> RaceNames = new Dictionary<int, string>
    {
        [1] = "Human",
        [2] = "Orc",
        [3] = "Dwarf",
        [4] = "Night Elf",
        [5] = "Undead",
        [6] = "Tauren",
        [7] = "Gnome",
        [8] = "Troll",
        [9] = "Goblin",
        [10] = "Blood Elf",
        [11] = "Draenei",
        [22] = "Worgen",
        [24] = "Pandaren",
        [25] = "Pandaren",
        [26] = "Pandaren",
        [27] = "Nightborne",
        [28] = "Highmountain Tauren",
        [29] = "Void Elf",
        [30] = "Lightforged Draenei",
        [34] = "Dark Iron Dwarf",
        [36] = "Mag'har Orc"
    };

    private static readonly IReadOnlyDictionary<int, string> ProfessionNamesById = new Dictionary<int, string>
    {
        [129] = "First Aid",
        [164] = "Blacksmithing",
        [165] = "Leatherworking",
        [171] = "Alchemy",
        [182] = "Herbalism",
        [185] = "Cooking",
        [186] = "Mining",
        [197] = "Tailoring",
        [202] = "Engineering",
        [333] = "Enchanting",
        [356] = "Fishing",
        [393] = "Skinning",
        [755] = "Jewelcrafting",
        [773] = "Inscription",
        [794] = "Archaeology"
    };

    private static readonly IReadOnlyDictionary<string, SimpleXlsxWriter.CellStyle> ClassCellStyles = BuildClassCellStyles();
    private static readonly IReadOnlyDictionary<string, SimpleXlsxWriter.CellStyle> WorkbookCellStyles = BuildWorkbookCellStyles();

    private readonly string _solutionRoot = Path.GetFullPath(solutionRoot);
    private readonly TauriApiOptions _apiOptions = apiOptions;

    public async Task<EndlessGuildExportResult> ExportAsync(string? requestedOutputPath, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        var apiUrl = BuildApiUrl(_apiOptions.BaseUrl, _apiOptions.ApiKey);
        var guildMembers = await LoadGuildMembersAsync(client, apiUrl, _apiOptions.Secret, cancellationToken);

        Console.WriteLine($"Found {guildMembers.Count} members in '{TargetGuildName}'.");
        Console.WriteLine("Fetching character-sheet details...");

        var rows = new ConcurrentBag<ExportRow>();
        var processedCount = 0;

        await Parallel.ForEachAsync(
            guildMembers,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxDegreeOfParallelism,
                CancellationToken = cancellationToken
            },
            async (member, ct) =>
            {
                var row = await BuildExportRowAsync(client, apiUrl, _apiOptions.Secret, member, ct);
                rows.Add(row);

                var processed = Interlocked.Increment(ref processedCount);
                if (processed % 10 == 0 || processed == guildMembers.Count)
                {
                    Console.WriteLine($"Loaded {processed}/{guildMembers.Count} members");
                }
            });

        var orderedRows = rows
            .OrderBy(row => row.GuildRank)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Level)
            .ToList();

        var outputPath = ResolveOutputPath(requestedOutputPath);
        var classStatisticNames = BuildClassStatisticNames();
        var rankFilters = BuildRankFilters(orderedRows);
        var sheetRows = BuildWorksheetRows(orderedRows, classStatisticNames, rankFilters);
        var dataValidations = BuildDataValidations(rankFilters.Count);

        await SimpleXlsxWriter.WriteSingleWorksheetAsync(
            outputPath,
            TargetGuildName,
            BuildHeaderRow(),
            sheetRows,
            WorkbookCellStyles,
            dataValidations,
            cancellationToken);

        var characterSheetCount = orderedRows.Count(row => row.CharacterSheetFetched);
        return new EndlessGuildExportResult(
            orderedRows.Count,
            characterSheetCount,
            orderedRows.Count - characterSheetCount,
            outputPath);
    }

    private async Task<List<GuildMemberRecord>> LoadGuildMembersAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        CancellationToken cancellationToken)
    {
        var response = await FetchResponseElementAsync(
            client,
            apiUrl,
            secret,
            "guild-info",
            new
            {
                r = TargetApiRealm,
                gn = TargetGuildName
            },
            cancellationToken);

        if (response is null ||
            !response.Value.TryGetProperty("guildList", out var guildListElement) ||
            guildListElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Guild '{TargetGuildName}' on {TargetDisplayRealm} was not found or returned no members.");
        }

        return guildListElement
            .EnumerateObject()
            .Select(property => property.Value)
            .Select(member => new GuildMemberRecord(
                Name: ReadString(member, "name"),
                Level: ReadInt(member, "level"),
                ClassId: ReadInt(member, "class"),
                RaceId: ReadInt(member, "race"),
                GuildRank: ReadInt(member, "rank"),
                GuildRankName: ReadString(member, "rank_name").Trim()))
            .Where(member => !string.IsNullOrWhiteSpace(member.Name))
            .OrderBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<ExportRow> BuildExportRowAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        GuildMemberRecord member,
        CancellationToken cancellationToken)
    {
        if (member.Name.Contains('#', StringComparison.Ordinal))
        {
            return CreateFallbackRow(member, "guild-info fallback: placeholder name");
        }

        try
        {
            var response = await FetchResponseElementAsync(
                client,
                apiUrl,
                secret,
                "character-sheet",
                new
                {
                    r = TargetApiRealm,
                    n = member.Name
                },
                cancellationToken);

            if (response is null)
            {
                return CreateFallbackRow(member, "guild-info fallback: character-sheet lookup failed");
            }

            var responseElement = response.Value;
            var classId = ReadInt(responseElement, "class", member.ClassId);
            var raceId = ReadInt(responseElement, "race", member.RaceId);
            var professions = ExtractProfessions(responseElement);

            return new ExportRow(
                Name: ReadString(responseElement, "name", member.Name),
                Realm: TargetDisplayRealm,
                Guild: ReadString(responseElement, "guildName", TargetGuildName),
                Level: ReadInt(responseElement, "level", member.Level),
                GuildRank: member.GuildRank,
                GuildRankName: member.GuildRankName,
                ClassId: classId,
                ClassName: LookupDisplayName(ClassNames, classId),
                RaceId: raceId,
                RaceName: LookupDisplayName(RaceNames, raceId),
                Profession1: professions.Profession1,
                Profession2: professions.Profession2,
                AchievementPoints: ReadInt(responseElement, "pts"),
                HonorableKills: ReadInt(responseElement, "playerHonorKills"),
                Faction: ReadString(responseElement, "faction_string_class"),
                Status: professions.FoundAny
                    ? "character-sheet"
                    : "character-sheet (no profession fields detected)",
                CharacterSheetFetched: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CreateFallbackRow(member, $"guild-info fallback: {ex.Message}");
        }
    }

    private static ExportRow CreateFallbackRow(GuildMemberRecord member, string status)
    {
        return new ExportRow(
            Name: member.Name,
            Realm: TargetDisplayRealm,
            Guild: TargetGuildName,
            Level: member.Level,
            GuildRank: member.GuildRank,
            GuildRankName: member.GuildRankName,
            ClassId: member.ClassId,
            ClassName: LookupDisplayName(ClassNames, member.ClassId),
            RaceId: member.RaceId,
            RaceName: LookupDisplayName(RaceNames, member.RaceId),
            Profession1: string.Empty,
            Profession2: string.Empty,
            AchievementPoints: 0,
            HonorableKills: 0,
            Faction: string.Empty,
            Status: status,
            CharacterSheetFetched: false);
    }

    private static ProfessionPair ExtractProfessions(JsonElement responseElement)
    {
        var candidates = new List<string>();

        foreach (var propertyName in DirectProfessionPropertyNames)
        {
            if (TryGetPropertyIgnoreCase(responseElement, propertyName, out var propertyValue) &&
                TryResolveProfessionName(propertyValue, out var professionName))
            {
                candidates.Add(professionName);
            }
        }

        foreach (var property in responseElement.EnumerateObject())
        {
            if (!ContainsProfessionKeyword(property.Name))
            {
                continue;
            }

            CollectProfessionCandidates(property.Value, candidates, 0);
        }

        var uniqueCandidates = candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var primaryProfessions = uniqueCandidates
            .Where(candidate => PrimaryProfessionNames.Contains(candidate))
            .ToList();

        var selected = primaryProfessions.Count > 0
            ? primaryProfessions
            : uniqueCandidates;

        return new ProfessionPair(
            selected.ElementAtOrDefault(0) ?? string.Empty,
            selected.ElementAtOrDefault(1) ?? string.Empty,
            uniqueCandidates.Count > 0);
    }

    private static void CollectProfessionCandidates(JsonElement element, List<string> candidates, int depth)
    {
        if (depth > 6)
        {
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (TryResolveProfessionFromObject(element, out var professionName))
                {
                    candidates.Add(professionName);
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (TryResolveProfessionFromProperty(property.Name, property.Value, out professionName))
                    {
                        candidates.Add(professionName);
                    }

                    CollectProfessionCandidates(property.Value, candidates, depth + 1);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectProfessionCandidates(item, candidates, depth + 1);
                }

                break;

            case JsonValueKind.String:
            case JsonValueKind.Number:
                if (TryResolveProfessionName(element, out professionName))
                {
                    candidates.Add(professionName);
                }

                break;
        }
    }

    private static bool TryResolveProfessionFromObject(JsonElement element, out string professionName)
    {
        professionName = string.Empty;

        string? candidateName = null;
        var candidateId = 0;

        foreach (var property in element.EnumerateObject())
        {
            switch (property.Name)
            {
                case "name":
                case "Name":
                case "skillName":
                case "SkillName":
                case "profession":
                case "professionName":
                case "ProfessionName":
                case "title":
                case "Title":
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        candidateName = property.Value.GetString();
                    }

                    break;

                case "id":
                case "Id":
                case "skillId":
                case "SkillId":
                case "skillLine":
                case "SkillLine":
                case "line":
                case "Line":
                    candidateId = ReadIntValue(property.Value);
                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(candidateName) &&
            TryNormalizeProfessionName(candidateName!, out professionName))
        {
            return true;
        }

        if (ProfessionNamesById.TryGetValue(candidateId, out var mappedProfessionName))
        {
            professionName = mappedProfessionName;
            return true;
        }

        return false;
    }

    private static bool TryResolveProfessionFromProperty(string propertyName, JsonElement propertyValue, out string professionName)
    {
        professionName = string.Empty;

        if (!ContainsProfessionKeyword(propertyName))
        {
            return false;
        }

        return TryResolveProfessionName(propertyValue, out professionName);
    }

    private static bool TryResolveProfessionName(JsonElement element, out string professionName)
    {
        professionName = string.Empty;

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return TryNormalizeProfessionName(element.GetString() ?? string.Empty, out professionName);

            case JsonValueKind.Number:
                if (ProfessionNamesById.TryGetValue(ReadIntValue(element), out var mappedProfessionName))
                {
                    professionName = mappedProfessionName;
                    return true;
                }

                return false;

            default:
                return false;
        }
    }

    private static bool TryNormalizeProfessionName(string rawValue, out string professionName)
    {
        professionName = string.Empty;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var value = rawValue.Trim();

        foreach (var knownProfession in KnownProfessionNames)
        {
            if (string.Equals(value, knownProfession, StringComparison.OrdinalIgnoreCase) ||
                value.Contains(knownProfession, StringComparison.OrdinalIgnoreCase))
            {
                professionName = knownProfession;
                return true;
            }
        }

        return false;
    }

    private static bool ContainsProfessionKeyword(string value)
    {
        return value.Contains("prof", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("skill", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("trade", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("primary", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("secondary", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<JsonElement?> FetchResponseElementAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        string endpoint,
        object parameters,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            secret,
            url = endpoint,
            @params = parameters
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync(apiUrl, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("response", out var responseElement))
        {
            return null;
        }

        return responseElement.Clone();
    }

    private static IReadOnlyList<SimpleXlsxWriter.CellData> BuildHeaderRow()
    {
        return
        [
            new("Name", HeaderStyleKey),
            new("Class", HeaderStyleKey),
            new("Race", HeaderStyleKey),
            new("Rank", HeaderStyleKey),
            new("Level", HeaderStyleKey),
            new("RankId", HeaderStyleKey),
            new(string.Empty),
            new("Class", HeaderStyleKey),
            new("Count", HeaderStyleKey),
            new(string.Empty),
            new("RankId", HeaderStyleKey),
            new("Rank", HeaderStyleKey),
            new("Include", HeaderStyleKey),
        ];
    }

    private static IReadOnlyList<IReadOnlyList<SimpleXlsxWriter.CellData>> BuildWorksheetRows(
        IReadOnlyList<ExportRow> orderedRows,
        IReadOnlyList<string> classStatisticNames,
        IReadOnlyList<RankFilterOption> rankFilters)
    {
        var totalRowCount = Math.Max(orderedRows.Count, Math.Max(classStatisticNames.Count, rankFilters.Count));
        var dataEndRow = orderedRows.Count + 1;
        var rankFilterEndRow = rankFilters.Count + 1;
        var sheetRows = new List<IReadOnlyList<SimpleXlsxWriter.CellData>>(totalRowCount);

        for (var index = 0; index < totalRowCount; index++)
        {
            var row = new List<SimpleXlsxWriter.CellData>(13);

            if (index < orderedRows.Count)
            {
                var exportRow = orderedRows[index];
                var classStyleKey = ClassCellStyles.ContainsKey(exportRow.ClassName)
                    ? exportRow.ClassName
                    : null;

                row.Add(new(exportRow.Name));
                row.Add(new(exportRow.ClassName, classStyleKey));
                row.Add(new(exportRow.RaceName));
                row.Add(new(exportRow.GuildRankName));
                row.Add(new(exportRow.Level.ToString()));
                row.Add(new(exportRow.GuildRank.ToString()));
            }
            else
            {
                row.AddRange(CreateEmptyCells(6));
            }

            row.Add(new(string.Empty));

            if (index < classStatisticNames.Count)
            {
                var className = classStatisticNames[index];
                var classStyleKey = ClassCellStyles.ContainsKey(className)
                    ? className
                    : null;

                row.Add(new(className, classStyleKey));
                row.Add(BuildClassCountFormulaCell(className, dataEndRow, rankFilterEndRow, orderedRows));
            }
            else
            {
                row.AddRange(CreateEmptyCells(2));
            }

            row.Add(new(string.Empty));

            if (index < rankFilters.Count)
            {
                var rankFilter = rankFilters[index];
                row.Add(new(rankFilter.RankId.ToString()));
                row.Add(new(rankFilter.RankName));
                row.Add(new(CheckedSymbol));
            }
            else
            {
                row.AddRange(CreateEmptyCells(3));
            }

            sheetRows.Add(row);
        }

        return sheetRows;
    }

    private string ResolveOutputPath(string? requestedOutputPath)
    {
        var downloadsDirectory = GetDownloadsDirectory();
        var fileName = string.IsNullOrWhiteSpace(requestedOutputPath)
            ? DefaultExportFileName
            : Path.GetFileName(requestedOutputPath.Trim());

        return Path.Combine(
            downloadsDirectory,
            string.IsNullOrWhiteSpace(fileName)
                ? DefaultExportFileName
                : fileName);
    }

    private static string GetDownloadsDirectory()
    {
        var userProfileDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfileDirectory))
        {
            throw new InvalidOperationException("Could not resolve the user profile directory for the export path.");
        }

        return Path.Combine(userProfileDirectory, "Downloads");
    }

    private static IReadOnlyList<string> BuildClassStatisticNames()
    {
        return
        [
            .. ClassColorHexByName.Keys,
            .. ClassNames.Values
                .Where(className => !ClassColorHexByName.ContainsKey(className))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(className => className, StringComparer.OrdinalIgnoreCase)
        ];
    }

    private static IReadOnlyList<RankFilterOption> BuildRankFilters(IReadOnlyList<ExportRow> orderedRows)
    {
        return orderedRows
            .Select(row => new RankFilterOption(row.GuildRank, row.GuildRankName))
            .Distinct()
            .OrderBy(rankFilter => rankFilter.RankId)
            .ThenBy(rankFilter => rankFilter.RankName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<SimpleXlsxWriter.DataValidation> BuildDataValidations(int rankFilterCount)
    {
        if (rankFilterCount <= 0)
        {
            return [];
        }

        return
        [
            new SimpleXlsxWriter.DataValidation(
                Sqref: $"M2:M{rankFilterCount + 1}",
                Formula1: $"\"{CheckedSymbol},{UncheckedSymbol}\"",
                AllowBlank: false)
        ];
    }

    private static SimpleXlsxWriter.CellData BuildClassCountFormulaCell(
        string className,
        int dataEndRow,
        int rankFilterEndRow,
        IReadOnlyList<ExportRow> orderedRows)
    {
        var formula =
            $"SUMPRODUCT(--($B$2:$B${dataEndRow}=\"{EscapeFormulaString(className)}\"),--(COUNTIFS($K$2:$K${rankFilterEndRow},$F$2:$F${dataEndRow},$M$2:$M${rankFilterEndRow},\"{CheckedSymbol}\")>0))";
        var cachedValue = orderedRows.Count(row => string.Equals(row.ClassName, className, StringComparison.OrdinalIgnoreCase));

        return new SimpleXlsxWriter.CellData(
            cachedValue.ToString(),
            ValueKind: SimpleXlsxWriter.CellValueKind.FormulaNumber,
            Formula: formula);
    }

    private static IEnumerable<SimpleXlsxWriter.CellData> CreateEmptyCells(int count)
    {
        for (var index = 0; index < count; index++)
        {
            yield return new SimpleXlsxWriter.CellData(string.Empty);
        }
    }

    private static string EscapeFormulaString(string value)
    {
        return value.Replace("\"", "\"\"", StringComparison.Ordinal);
    }

    private static string LookupDisplayName(IReadOnlyDictionary<int, string> map, int id)
    {
        return map.TryGetValue(id, out var name)
            ? name
            : id > 0
                ? $"Unknown ({id})"
                : string.Empty;
    }

    private static IReadOnlyDictionary<string, SimpleXlsxWriter.CellStyle> BuildClassCellStyles()
    {
        var styles = new Dictionary<string, SimpleXlsxWriter.CellStyle>(StringComparer.OrdinalIgnoreCase);

        foreach (var classColor in ClassColorHexByName)
        {
            styles[classColor.Key] = new SimpleXlsxWriter.CellStyle(
                FillColorHex: classColor.Value,
                FontColorHex: "000000");
        }

        return styles;
    }

    private static IReadOnlyDictionary<string, SimpleXlsxWriter.CellStyle> BuildWorkbookCellStyles()
    {
        var styles = new Dictionary<string, SimpleXlsxWriter.CellStyle>(StringComparer.OrdinalIgnoreCase)
        {
            [HeaderStyleKey] = new SimpleXlsxWriter.CellStyle(
                FillColorHex: "C0C0C0",
                FontColorHex: "000000",
                IsBold: true)
        };

        foreach (var classStyle in ClassCellStyles)
        {
            styles[classStyle.Key] = classStyle.Value;
        }

        return styles;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement parent, string propertyName, out JsonElement value)
    {
        foreach (var property in parent.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static int ReadInt(JsonElement parent, string propertyName, int defaultValue = 0)
    {
        return parent.TryGetProperty(propertyName, out var property)
            ? ReadIntValue(property, defaultValue)
            : defaultValue;
    }

    private static int ReadIntValue(JsonElement value, int defaultValue = 0)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var intValue))
        {
            return intValue;
        }

        return value.ValueKind == JsonValueKind.String &&
               int.TryParse(value.GetString(), out intValue)
            ? intValue
            : defaultValue;
    }

    private static string ReadString(JsonElement parent, string propertyName, string defaultValue = "")
    {
        if (!parent.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return defaultValue;
        }

        return property.GetString()?.Trim() ?? defaultValue;
    }

    private static string BuildApiUrl(string baseUrl, string apiKey)
    {
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseUrl}{separator}apikey={Uri.EscapeDataString(apiKey)}";
    }

    private readonly record struct GuildMemberRecord(
        string Name,
        int Level,
        int ClassId,
        int RaceId,
        int GuildRank,
        string GuildRankName);

    private readonly record struct RankFilterOption(
        int RankId,
        string RankName);

    private readonly record struct ProfessionPair(
        string Profession1,
        string Profession2,
        bool FoundAny);

    private readonly record struct ExportRow(
        string Name,
        string Realm,
        string Guild,
        int Level,
        int GuildRank,
        string GuildRankName,
        int ClassId,
        string ClassName,
        int RaceId,
        string RaceName,
        string Profession1,
        string Profession2,
        int AchievementPoints,
        int HonorableKills,
        string Faction,
        string Status,
        bool CharacterSheetFetched);
}
