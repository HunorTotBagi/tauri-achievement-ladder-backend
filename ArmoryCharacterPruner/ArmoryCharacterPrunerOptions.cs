namespace ArmoryCharacterPruner;

public sealed record ArmoryCharacterPrunerOptions(
    string NamesFilePath,
    string? Realm,
    string? OutputPath,
    int MaxDegreeOfParallelism)
{
    private const int DefaultParallelism = 10;

    public static string UsageText =>
        """
        Usage:
          dotnet run --project ArmoryCharacterPruner -- --names-file s15_6
          dotnet run --project ArmoryCharacterPruner -- --names-file .\RareAchiAndItemScan\Input\tauri-ban-list.txt --realm Tauri
          dotnet run --project ArmoryCharacterPruner -- --names-file s15_6 --output .\filtered-s15_6.txt
          dotnet run --project ArmoryCharacterPruner -- --names-file s15_6 --parallelism 5

        Options:
          --names-file <path>   Required. Accepts a real path or a bare batch file name such as s15_6.
          --realm <realm>       Optional fallback realm for lines that contain only a character name.
          --output <path>       Optional output path. Defaults to rewriting the input file in place.
          --parallelism <n>     Optional. Lower values reduce lockout risk. Default: 10.
          --help                Show this help text.
        """;

    public string DescribeScope() =>
        string.IsNullOrWhiteSpace(Realm)
            ? $"characters from {Path.GetFileName(NamesFilePath)}"
            : $"characters from {Path.GetFileName(NamesFilePath)} with fallback realm {Realm}";

    public static bool TryParse(
        string[] args,
        out ArmoryCharacterPrunerOptions? options,
        out string? errorMessage,
        out bool showHelp)
    {
        options = null;
        errorMessage = null;
        showHelp = false;

        string? namesFilePath = null;
        string? realm = null;
        string? outputPath = null;
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

                case "--output":
                    if (!TryReadValue(args, ref index, argument, out outputPath, out errorMessage))
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

                default:
                    errorMessage = $"Unknown option: {argument}";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(namesFilePath))
        {
            errorMessage = "--names-file is required.";
            return false;
        }

        options = new ArmoryCharacterPrunerOptions(
            namesFilePath.Trim(),
            realm?.Trim(),
            outputPath?.Trim(),
            maxDegreeOfParallelism);

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
}
