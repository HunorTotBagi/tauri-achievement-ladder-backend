namespace MissingItemFinder;

public sealed record MissingItemFinderOptions(
    IReadOnlyList<int> ItemIds,
    string? CharacterName,
    string? NamesFilePath,
    string? Realm,
    int MaxDegreeOfParallelism,
    string? OutputPath,
    string? MissingOutputPath)
{
    private const int DefaultParallelism = 5;

    public bool HasSpecificCharacter => !string.IsNullOrWhiteSpace(CharacterName);
    public bool HasNamesFile => !string.IsNullOrWhiteSpace(NamesFilePath);

    public static string UsageText =>
        """
        Usage:
          dotnet run --project MissingItemFinder -- --item-ids "73712,73709,73710"
          dotnet run --project MissingItemFinder -- --item-ids "73712,73709" --name Larahh --realm Tauri
          dotnet run --project MissingItemFinder -- --item-ids "73712,73709" --names-file .\MissingItemCharactersToScan.txt
          dotnet run --project MissingItemFinder -- --item-ids "73712,73709" --names-file .\..\RareAchiAndItemScan\Input\tauri-ban-list.txt --realm Tauri
          dotnet run --project MissingItemFinder -- --item-ids "73712,73709" --parallelism 3

        Options:
          --item-ids <ids>      Required. Comma-separated item IDs to match.
          --name <character>    Optional. Scan only one character. Requires --realm.
          --names-file <path>   Optional. Scan only characters from a text file instead of all default source characters.
          --realm <realm>       Optional fallback realm for --names-file lines that do not already end in -Realm.
          --parallelism <n>     Optional. Lower values reduce lockout risk. Default: 5.
          --output <path>       Optional JSON report path. Relative paths are resolved from MissingItemFinder.
          --missing-output <path>
                                Optional retry text path. Relative paths are resolved from MissingItemFinder.
          --help                Show this help text.
        """;

    public string DescribeScope()
    {
        if (HasSpecificCharacter)
        {
            return $"{CharacterName} on {Realm}";
        }

        if (!HasNamesFile)
        {
            return "all source characters";
        }

        return string.IsNullOrWhiteSpace(Realm)
            ? $"characters from {Path.GetFileName(NamesFilePath)}"
            : $"characters from {Path.GetFileName(NamesFilePath)} with fallback realm {Realm}";
    }

    public string DescribeItems() =>
        $"item IDs {string.Join(", ", ItemIds)}";

    public static bool TryParse(
        string[] args,
        out MissingItemFinderOptions? options,
        out string? errorMessage,
        out bool showHelp)
    {
        options = null;
        errorMessage = null;
        showHelp = false;

        IReadOnlyList<int> itemIds = Array.Empty<int>();
        string? characterName = null;
        string? namesFilePath = null;
        string? realm = null;
        string? outputPath = null;
        string? missingOutputPath = null;
        var maxDegreeOfParallelism = DefaultParallelism;

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

                case "--item-ids":
                case "--item-id":
                    if (!TryReadValue(args, ref index, argument, out var rawItemIds, out errorMessage))
                    {
                        return false;
                    }

                    if (!TryParseItemIds(rawItemIds!, out itemIds, out errorMessage))
                    {
                        return false;
                    }

                    break;

                case "--name":
                    if (!TryReadValue(args, ref index, argument, out characterName, out errorMessage))
                    {
                        return false;
                    }

                    break;

                case "--names-file":
                    if (!TryReadValue(args, ref index, argument, out namesFilePath, out errorMessage))
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

                case "--parallelism":
                    if (!TryReadValue(args, ref index, argument, out var rawParallelism, out errorMessage))
                    {
                        return false;
                    }

                    if (!int.TryParse(rawParallelism, out maxDegreeOfParallelism) || maxDegreeOfParallelism <= 0)
                    {
                        errorMessage = $"Invalid parallelism value: {rawParallelism}";
                        return false;
                    }

                    break;

                case "--output":
                    if (!TryReadValue(args, ref index, argument, out outputPath, out errorMessage))
                    {
                        return false;
                    }

                    break;

                case "--missing-output":
                    if (!TryReadValue(args, ref index, argument, out missingOutputPath, out errorMessage))
                    {
                        return false;
                    }

                    break;

                default:
                    errorMessage = $"Unknown option: {argument}";
                    return false;
            }
        }

        if (itemIds.Count == 0)
        {
            errorMessage = "--item-ids is required.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(characterName) && !string.IsNullOrWhiteSpace(namesFilePath))
        {
            errorMessage = "--name and --names-file cannot be used together.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(characterName) && string.IsNullOrWhiteSpace(realm))
        {
            errorMessage = "--realm is required with --name.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(characterName) &&
            string.IsNullOrWhiteSpace(namesFilePath) &&
            !string.IsNullOrWhiteSpace(realm))
        {
            errorMessage = "--realm can only be used together with --name or --names-file.";
            return false;
        }

        options = new MissingItemFinderOptions(
            itemIds,
            characterName?.Trim(),
            namesFilePath?.Trim(),
            realm?.Trim(),
            maxDegreeOfParallelism,
            outputPath?.Trim(),
            missingOutputPath?.Trim());

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

    private static bool TryParseItemIds(
        string rawItemIds,
        out IReadOnlyList<int> itemIds,
        out string? errorMessage)
    {
        itemIds = Array.Empty<int>();
        errorMessage = null;

        var tokens = rawItemIds.Split(
            [',', ';', '|', ' '],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
        {
            errorMessage = "At least one item ID must be provided.";
            return false;
        }

        var results = new List<int>(tokens.Length);
        var seen = new HashSet<int>();

        foreach (var token in tokens)
        {
            if (!int.TryParse(token, out var itemId) || itemId <= 0)
            {
                errorMessage = $"Invalid item ID: {token}";
                return false;
            }

            if (seen.Add(itemId))
            {
                results.Add(itemId);
            }
        }

        itemIds = results;
        return true;
    }
}
