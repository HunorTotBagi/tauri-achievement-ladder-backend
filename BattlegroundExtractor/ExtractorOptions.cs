namespace BattlegroundExtractor;

public sealed class ExtractorOptions
{
    private static readonly (string[] Aliases, string ApiRealm, string DisplayRealm)[] RealmAliases =
    [
        (["evermoon", "[EN] Evermoon"], "[EN] Evermoon", "Evermoon"),
        (["tauri", "[HU] Tauri WoW Server"], "[HU] Tauri WoW Server", "Tauri"),
        (["wod", "[HU] Warriors of Darkness"], "[HU] Warriors of Darkness", "WoD")
    ];

    private ExtractorOptions(string matchId, string apiRealm, string displayRealm)
    {
        MatchId = matchId;
        ApiRealm = apiRealm;
        DisplayRealm = displayRealm;
    }

    public string MatchId { get; }

    public string ApiRealm { get; }

    public string DisplayRealm { get; }

    public static string UsageText =>
        """
        BattlegroundExtractor — scans a battleground match for rare achievements and unknown guilds.

        Usage:
          BattlegroundExtractor <matchid> [realm]

        Arguments:
          matchid    The pvp-match id to extract (for example 95874).
          realm      Optional realm used to query the match. Defaults to evermoon.
                     Accepts: evermoon, tauri, wod (or a full API realm name).

        Examples:
          BattlegroundExtractor 95874
          BattlegroundExtractor 95874 tauri
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
            errorMessage = "A matchid is required.";
            return false;
        }

        if (args.Length == 1 &&
            (args[0] is "-h" or "--help" or "/?" || string.Equals(args[0], "help", StringComparison.OrdinalIgnoreCase)))
        {
            showHelp = true;
            return false;
        }

        var matchId = args[0].Trim();
        if (string.IsNullOrWhiteSpace(matchId) || !matchId.All(char.IsDigit))
        {
            errorMessage = $"Invalid matchid '{args[0]}'. The matchid must be numeric.";
            return false;
        }

        var realmInput = args.Length > 1 ? args[1].Trim() : "evermoon";
        if (!TryResolveRealm(realmInput, out var apiRealm, out var displayRealm))
        {
            errorMessage = $"Unknown realm '{realmInput}'. Valid values: evermoon, tauri, wod, or a full API realm name.";
            return false;
        }

        options = new ExtractorOptions(matchId, apiRealm, displayRealm);
        return true;
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
