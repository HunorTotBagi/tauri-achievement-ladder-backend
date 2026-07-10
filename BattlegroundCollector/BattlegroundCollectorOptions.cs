using System.Globalization;

namespace BattlegroundCollector;

public sealed record BattlegroundCollectorOptions(
    int? StartMatchId,
    string ApiRealm,
    string DisplayRealm,
    string? OutputPath,
    string? StatePath
)
{
    private static readonly (
        string[] Aliases,
        string ApiRealm,
        string DisplayRealm
    )[] RealmAliases =
    [
        (["evermoon", "[EN] Evermoon"], "[EN] Evermoon", "Evermoon"),
        (["tauri", "[HU] Tauri WoW Server"], "[HU] Tauri WoW Server", "Tauri"),
        (["wod", "[HU] Warriors of Darkness"], "[HU] Warriors of Darkness", "WoD"),
    ];

    public static string UsageText =>
        """
            BattlegroundCollector - collects battleground match metadata into JSON.

            Usage:
              dotnet run --project BattlegroundCollector -- <startMatchId> [realm]
              dotnet run --project BattlegroundCollector
              dotnet run --project BattlegroundCollector -- --start <startMatchId> --realm evermoon

            Arguments:
              startMatchId   Optional after the first run. The first match id to try.
                             If omitted, the saved state file decides where to resume.
              realm          Optional realm used to query matches. Defaults to evermoon.
                             Accepts: evermoon, tauri, wod, or a full API realm name.

            Options:
              --start <id>   Same as the positional startMatchId.
              --realm <name> Same as the positional realm.
              --output <path>
                             Optional JSON output path. Relative paths are resolved from BattlegroundCollector.
              --state <path> Optional resume-state JSON path. Relative paths are resolved from BattlegroundCollector.
              --help         Show this help text.
            """;

    public static bool TryParse(
        string[] args,
        out BattlegroundCollectorOptions? options,
        out string? errorMessage,
        out bool showHelp
    )
    {
        options = null;
        errorMessage = null;
        showHelp = false;

        int? startMatchId = null;
        var realmInput = "evermoon";
        var hasRealm = false;
        string? outputPath = null;
        string? statePath = null;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            switch (argument)
            {
                case "--help":
                case "-h":
                case "/?":
                    showHelp = true;
                    return false;

                case "--start":
                case "--start-match-id":
                    if (
                        !TryReadValue(args, ref index, argument, out var rawStart, out errorMessage)
                    )
                    {
                        return false;
                    }

                    if (!TryParseMatchId(rawStart!, out var parsedStart))
                    {
                        errorMessage = $"Invalid start match id: {rawStart}";
                        return false;
                    }

                    startMatchId = parsedStart;
                    break;

                case "--realm":
                    if (
                        !TryReadValue(args, ref index, argument, out var rawRealm, out errorMessage)
                    )
                    {
                        return false;
                    }

                    realmInput = rawRealm!;
                    hasRealm = true;
                    break;

                case "--output":
                    if (!TryReadValue(args, ref index, argument, out outputPath, out errorMessage))
                    {
                        return false;
                    }

                    break;

                case "--state":
                    if (!TryReadValue(args, ref index, argument, out statePath, out errorMessage))
                    {
                        return false;
                    }

                    break;

                default:
                    if (argument.StartsWith("--", StringComparison.Ordinal))
                    {
                        errorMessage = $"Unknown option: {argument}";
                        return false;
                    }

                    if (TryParseMatchId(argument, out var positionalStart) && startMatchId is null)
                    {
                        startMatchId = positionalStart;
                        break;
                    }

                    if (!hasRealm)
                    {
                        realmInput = argument;
                        hasRealm = true;
                        break;
                    }

                    errorMessage = $"Unknown argument: {argument}";
                    return false;
            }
        }

        if (!TryResolveRealm(realmInput, out var apiRealm, out var displayRealm))
        {
            errorMessage =
                $"Unknown realm '{realmInput}'. Valid values: evermoon, tauri, wod, or a full API realm name.";
            return false;
        }

        options = new BattlegroundCollectorOptions(
            startMatchId,
            apiRealm,
            displayRealm,
            outputPath?.Trim(),
            statePath?.Trim()
        );

        return true;
    }

    private static bool TryReadValue(
        string[] args,
        ref int index,
        string optionName,
        out string? value,
        out string? errorMessage
    )
    {
        value = null;
        errorMessage = null;

        if (index + 1 >= args.Length)
        {
            errorMessage = $"Missing value for {optionName}.";
            return false;
        }

        value = args[++index];
        return true;
    }

    private static bool TryParseMatchId(string value, out int matchId)
    {
        // NumberStyles.None allows digits only (no sign, whitespace, or separators).
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out matchId)
            && matchId > 0;
    }

    private static bool TryResolveRealm(
        string realmInput,
        out string apiRealm,
        out string displayRealm
    )
    {
        foreach (var (aliases, mappedApiRealm, mappedDisplayRealm) in RealmAliases)
        {
            if (
                aliases.Any(alias =>
                    string.Equals(alias, realmInput, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                apiRealm = mappedApiRealm;
                displayRealm = mappedDisplayRealm;
                return true;
            }
        }

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
