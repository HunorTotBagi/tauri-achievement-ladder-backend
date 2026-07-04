namespace RaidLogCharacterRareAchiFinder;

public sealed record RaidLogFinderOptions(
    int? StartLogId,
    string ApiRealm,
    string DisplayRealm,
    string? StatePath,
    int? MaxLogs,
    int ScanParallelism)
{
    private const int DefaultScanParallelism = 4;

    private static readonly (string[] Aliases, string ApiRealm, string DisplayRealm)[] RealmAliases =
    [
        (["evermoon", "[EN] Evermoon"], "[EN] Evermoon", "Evermoon"),
        (["tauri", "[HU] Tauri WoW Server"], "[HU] Tauri WoW Server", "Tauri"),
        (["wod", "[HU] Warriors of Darkness"], "[HU] Warriors of Darkness", "WoD")
    ];

    public static string UsageText =>
        """
        RaidLogCharacterRareAchiFinder - scans raid-log members for rare achievements, items, mounts, and unknown guilds.

        Usage:
          dotnet run --project RaidLogCharacterRareAchiFinder
          dotnet run --project RaidLogCharacterRareAchiFinder -- --start 1
          dotnet run --project RaidLogCharacterRareAchiFinder -- --start 126 --realm tauri

        Options:
          --start <id>       Optional first raid-log id. If omitted, the saved state is used;
                             if no state exists, scanning starts at id 1.
          --realm <realm>    Optional realm used for the raid-log endpoint. Defaults to tauri.
                             Accepts: tauri, evermoon, wod, or a full API realm name.
          --state <path>     Optional txt checkpoint path. Relative paths are resolved from this project.
          --max-logs <n>     Optional safety limit for one run.
          --parallelism <n>  Optional character scan parallelism. Default: 4.
          --help             Show this help text.
        """;

    public static bool TryParse(
        string[] args,
        out RaidLogFinderOptions? options,
        out string? errorMessage,
        out bool showHelp)
    {
        options = null;
        errorMessage = null;
        showHelp = false;

        int? startLogId = null;
        var realmInput = "tauri";
        var hasRealm = false;
        string? statePath = null;
        int? maxLogs = null;
        var scanParallelism = DefaultScanParallelism;

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
                case "--start-log-id":
                    if (!TryReadValue(args, ref index, argument, out var rawStart, out errorMessage))
                    {
                        return false;
                    }

                    if (!TryParsePositiveInt(rawStart!, out var parsedStart))
                    {
                        errorMessage = $"Invalid start raid-log id: {rawStart}";
                        return false;
                    }

                    startLogId = parsedStart;
                    break;

                case "--realm":
                    if (!TryReadValue(args, ref index, argument, out var rawRealm, out errorMessage))
                    {
                        return false;
                    }

                    realmInput = rawRealm!;
                    hasRealm = true;
                    break;

                case "--state":
                    if (!TryReadValue(args, ref index, argument, out statePath, out errorMessage))
                    {
                        return false;
                    }

                    break;

                case "--max-logs":
                    if (!TryReadValue(args, ref index, argument, out var rawMaxLogs, out errorMessage))
                    {
                        return false;
                    }

                    if (!TryParsePositiveInt(rawMaxLogs!, out var parsedMaxLogs))
                    {
                        errorMessage = $"Invalid max log count: {rawMaxLogs}";
                        return false;
                    }

                    maxLogs = parsedMaxLogs;
                    break;

                case "--parallelism":
                    if (!TryReadValue(args, ref index, argument, out var rawParallelism, out errorMessage))
                    {
                        return false;
                    }

                    if (!TryParsePositiveInt(rawParallelism!, out scanParallelism))
                    {
                        errorMessage = $"Invalid parallelism value: {rawParallelism}";
                        return false;
                    }

                    break;

                default:
                    if (argument.StartsWith("--", StringComparison.Ordinal))
                    {
                        errorMessage = $"Unknown option: {argument}";
                        return false;
                    }

                    if (TryParsePositiveInt(argument, out var positionalStart) && startLogId is null)
                    {
                        startLogId = positionalStart;
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
            errorMessage = $"Unknown realm '{realmInput}'. Valid values: tauri, evermoon, wod, or a full API realm name.";
            return false;
        }

        options = new RaidLogFinderOptions(
            startLogId,
            apiRealm,
            displayRealm,
            statePath?.Trim(),
            maxLogs,
            scanParallelism);

        return true;
    }

    private static bool TryReadValue(
        string[] args,
        ref int index,
        string optionName,
        out string? value,
        out string? errorMessage)
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

    private static bool TryParsePositiveInt(string value, out int result)
    {
        result = 0;
        return !string.IsNullOrWhiteSpace(value) &&
               value.All(char.IsDigit) &&
               int.TryParse(value, out result) &&
               result > 0;
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
