namespace BattlegroundExtractor;

public sealed class ExtractorOptions
{
    private static readonly (string[] Aliases, string ApiRealm, string DisplayRealm)[] RealmAliases =
    [
        (["evermoon", "[EN] Evermoon"], "[EN] Evermoon", "Evermoon"),
        (["tauri", "[HU] Tauri WoW Server"], "[HU] Tauri WoW Server", "Tauri"),
        (["wod", "[HU] Warriors of Darkness"], "[HU] Warriors of Darkness", "WoD")
    ];

    private ExtractorOptions(int startMatchId, int endMatchId, string apiRealm, string displayRealm)
    {
        StartMatchId = startMatchId;
        EndMatchId = endMatchId;
        ApiRealm = apiRealm;
        DisplayRealm = displayRealm;
    }

    public int StartMatchId { get; }

    public int EndMatchId { get; }

    public int MatchCount => EndMatchId - StartMatchId + 1;

    public bool IsRange => EndMatchId > StartMatchId;

    public string ApiRealm { get; }

    public string DisplayRealm { get; }

    public IEnumerable<int> MatchIds
    {
        get
        {
            for (var id = StartMatchId; id <= EndMatchId; id++)
            {
                yield return id;
            }
        }
    }

    public string DescribeRange =>
        IsRange ? $"{StartMatchId}-{EndMatchId} ({MatchCount} matches)" : StartMatchId.ToString();

    public static string UsageText =>
        """
        BattlegroundExtractor — scans battleground match(es) for rare achievements and unknown guilds.

        Usage:
          BattlegroundExtractor <matchid> [realm]
          BattlegroundExtractor <start>-<end> [realm]
          BattlegroundExtractor <start> <end> [realm]

        Arguments:
          matchid      A single pvp-match id to extract (for example 95874).
          start, end   An inclusive range of match ids to loop over (for example 94000 95880).
          realm        Optional realm used to query the matches. Defaults to evermoon.
                       Accepts: evermoon, tauri, wod (or a full API realm name).

        Examples:
          BattlegroundExtractor 95874
          BattlegroundExtractor 94000-95880
          BattlegroundExtractor 94000 95880 tauri
        """;

    public static bool TryParse(
        string[] args,
        out ExtractorOptions? options,
        out string? errorMessage,
        out bool showHelp)
    {
        options = null;
        errorMessage = null;
        showHelp = false;

        if (args.Length == 0)
        {
            errorMessage = "A matchid or match id range is required.";
            return false;
        }

        if (args.Length == 1 &&
            (args[0] is "-h" or "--help" or "/?" || string.Equals(args[0], "help", StringComparison.OrdinalIgnoreCase)))
        {
            showHelp = true;
            return false;
        }

        var remaining = new Queue<string>(args);
        var first = remaining.Dequeue().Trim();

        int startMatchId;
        int endMatchId;

        // Form: <start>-<end> as a single token.
        var hyphenIndex = first.IndexOf('-');
        if (hyphenIndex > 0)
        {
            var startText = first[..hyphenIndex].Trim();
            var endText = first[(hyphenIndex + 1)..].Trim();
            if (!TryParseMatchId(startText, out startMatchId) || !TryParseMatchId(endText, out endMatchId))
            {
                errorMessage = $"Invalid range '{first}'. Use numeric ids like 94000-95880.";
                return false;
            }
        }
        else
        {
            if (!TryParseMatchId(first, out startMatchId))
            {
                errorMessage = $"Invalid matchid '{first}'. The matchid must be numeric.";
                return false;
            }

            // Form: <start> <end> as two tokens — only when the next token is numeric.
            if (remaining.Count > 0 && TryParseMatchId(remaining.Peek().Trim(), out var parsedEnd))
            {
                remaining.Dequeue();
                endMatchId = parsedEnd;
            }
            else
            {
                endMatchId = startMatchId;
            }
        }

        if (endMatchId < startMatchId)
        {
            errorMessage = $"Invalid range: end ({endMatchId}) is less than start ({startMatchId}).";
            return false;
        }

        var realmInput = remaining.Count > 0 ? remaining.Dequeue().Trim() : "evermoon";
        if (!TryResolveRealm(realmInput, out var apiRealm, out var displayRealm))
        {
            errorMessage = $"Unknown realm '{realmInput}'. Valid values: evermoon, tauri, wod, or a full API realm name.";
            return false;
        }

        options = new ExtractorOptions(startMatchId, endMatchId, apiRealm, displayRealm);
        return true;
    }

    private static bool TryParseMatchId(string value, out int matchId)
    {
        matchId = 0;
        return !string.IsNullOrWhiteSpace(value) &&
               value.All(char.IsDigit) &&
               int.TryParse(value, out matchId) &&
               matchId > 0;
    }

    private static bool TryResolveRealm(string realmInput, out string apiRealm, out string displayRealm)
    {
        foreach (var (aliases, mappedApiRealm, mappedDisplayRealm) in RealmAliases)
        {
            if (aliases.Any(alias => string.Equals(alias, realmInput, StringComparison.OrdinalIgnoreCase)))
            {
                apiRealm = mappedApiRealm;
                displayRealm = mappedDisplayRealm;
                return true;
            }
        }

        // Allow any full API realm string (cross-realm names not in the known list).
        if (realmInput.StartsWith('[') && realmInput.Contains(']'))
        {
            apiRealm = realmInput;
            displayRealm = realmInput;
            return true;
        }

        apiRealm = string.Empty;
        displayRealm = string.Empty;
        return false;
    }
}
