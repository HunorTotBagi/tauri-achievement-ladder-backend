namespace RareAchiAndItemScan;

[Flags]
public enum ScanTarget
{
    None = 0,
    Achievements = 1,
    Items = 2,
    Mounts = 4,
    All = Achievements | Items | Mounts
}

public sealed record ScanOptions(
    string? CharacterName,
    string? GuildName,
    string? Realm,
    ScanTarget Targets,
    string? OutputPath)
{
    public bool HasSpecificCharacter => !string.IsNullOrWhiteSpace(CharacterName);
    public bool HasSpecificGuild => !string.IsNullOrWhiteSpace(GuildName);

    public static string UsageText =>
        """
        Usage:
          dotnet run --project RareAchiAndItemScan
          dotnet run --project RareAchiAndItemScan -- --name Larahh --realm Tauri
          dotnet run --project RareAchiAndItemScan -- --guild "Outlaws" --realm Tauri
          dotnet run --project RareAchiAndItemScan -- --scan achievements,items
          dotnet run --project RareAchiAndItemScan -- --scan mounts --output .\reports\rare-mounts.json

        Options:
          --name <character>    Scan a single character instead of all source characters.
          --guild <name>        Scan every member of one guild on the selected realm.
          --realm <realm>       Required with --name or --guild. Accepts Tauri, Evermoon, WoD, or the full API realm name.
          --scan <targets>      Comma-separated: achievements, items, mounts, or all. Default: all.
          --output <path>       Optional output file path. Relative paths are resolved from RareAchiAndItemScan.
          --help                Show this help text.
        """;

    public string DescribeScope()
    {
        return HasSpecificCharacter
            ? $"{CharacterName} on {Realm}"
            : HasSpecificGuild
                ? $"guild {GuildName} on {Realm}"
                : "all source characters";
    }

    public string DescribeTargets()
    {
        if (Targets == ScanTarget.All)
        {
            return "achievements, items, and mounts";
        }

        var parts = new List<string>();
        if (Targets.HasFlag(ScanTarget.Achievements))
        {
            parts.Add("achievements");
        }

        if (Targets.HasFlag(ScanTarget.Items))
        {
            parts.Add("items");
        }

        if (Targets.HasFlag(ScanTarget.Mounts))
        {
            parts.Add("mounts");
        }

        return string.Join(", ", parts);
    }

    public string GetTargetFileSegment()
    {
        if (Targets == ScanTarget.All)
        {
            return "all";
        }

        var parts = new List<string>();
        if (Targets.HasFlag(ScanTarget.Achievements))
        {
            parts.Add("achievements");
        }

        if (Targets.HasFlag(ScanTarget.Items))
        {
            parts.Add("items");
        }

        if (Targets.HasFlag(ScanTarget.Mounts))
        {
            parts.Add("mounts");
        }

        return string.Join("-", parts);
    }

    public static bool TryParse(
        string[] args,
        out ScanOptions? options,
        out string? errorMessage,
        out bool showHelp)
    {
        options = null;
        errorMessage = null;
        showHelp = false;

        string? characterName = null;
        string? guildName = null;
        string? realm = null;
        string? outputPath = null;
        var targets = ScanTarget.All;

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

                case "--name":
                    if (!TryReadValue(args, ref index, argument, out characterName, out errorMessage))
                    {
                        return false;
                    }

                    break;

                case "--realm":
                    if (!TryReadValue(args, ref index, argument, out realm, out errorMessage))
                    {
                        return false;
                    }

                    break;

                case "--guild":
                    if (!TryReadValue(args, ref index, argument, out guildName, out errorMessage))
                    {
                        return false;
                    }

                    break;

                case "--scan":
                    if (!TryReadValue(args, ref index, argument, out var rawTargets, out errorMessage))
                    {
                        return false;
                    }

                    if (!TryParseTargets(rawTargets!, out targets, out errorMessage))
                    {
                        return false;
                    }

                    break;

                case "--output":
                    if (!TryReadValue(args, ref index, argument, out outputPath, out errorMessage))
                    {
                        return false;
                    }

                    break;

                default:
                    errorMessage = $"Unknown option: {argument}";
                    return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(characterName) && !string.IsNullOrWhiteSpace(guildName))
        {
            errorMessage = "--name and --guild cannot be used together.";
            return false;
        }

        if (( !string.IsNullOrWhiteSpace(characterName) || !string.IsNullOrWhiteSpace(guildName)) &&
            string.IsNullOrWhiteSpace(realm))
        {
            errorMessage = "--realm is required when --name or --guild is provided.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(characterName) &&
            string.IsNullOrWhiteSpace(guildName) &&
            !string.IsNullOrWhiteSpace(realm))
        {
            errorMessage = "--realm can only be used together with --name or --guild.";
            return false;
        }

        options = new ScanOptions(
            characterName?.Trim(),
            guildName?.Trim(),
            realm?.Trim(),
            targets,
            outputPath?.Trim());

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

    private static bool TryParseTargets(string rawTargets, out ScanTarget targets, out string? errorMessage)
    {
        targets = ScanTarget.None;
        errorMessage = null;

        var tokens = rawTargets.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            errorMessage = "At least one scan target must be provided.";
            return false;
        }

        foreach (var token in tokens)
        {
            switch (token.ToLowerInvariant())
            {
                case "all":
                    targets = ScanTarget.All;
                    return true;

                case "achievement":
                case "achievements":
                case "achi":
                    targets |= ScanTarget.Achievements;
                    break;

                case "item":
                case "items":
                    targets |= ScanTarget.Items;
                    break;

                case "mount":
                case "mounts":
                    targets |= ScanTarget.Mounts;
                    break;

                default:
                    errorMessage = $"Unknown scan target: {token}";
                    return false;
            }
        }

        if (targets == ScanTarget.None)
        {
            errorMessage = "At least one valid scan target must be provided.";
            return false;
        }

        return true;
    }
}
