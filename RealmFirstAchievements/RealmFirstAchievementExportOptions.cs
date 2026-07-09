namespace RealmFirstAchievements;

public sealed record RealmFirstAchievementExportOptions(int? Parallelism)
{
    public static string UsageText =>
        """
            RealmFirstAchievements - exports valid realm-first characters for Evermoon, Tauri, and WoD.

            Usage:
              dotnet run --project RealmFirstAchievements
              dotnet run --project RealmFirstAchievements -- --parallelism 30

            Options:
              --parallelism <n>  Optional API request parallelism for this run.
                                 Defaults to TauriApi.MaxConcurrentRequests from configuration.
              --help            Show this help text.
            """;

    public static bool TryParse(
        string[] args,
        out RealmFirstAchievementExportOptions? options,
        out string? errorMessage,
        out bool showHelp
    )
    {
        options = null;
        errorMessage = null;
        showHelp = false;

        int? parallelism = null;

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

                case "--parallelism":
                    if (
                        !TryReadValue(
                            args,
                            ref index,
                            argument,
                            out var rawParallelism,
                            out errorMessage
                        )
                    )
                    {
                        return false;
                    }

                    if (!TryParsePositiveInt(rawParallelism!, out var parsedParallelism))
                    {
                        errorMessage = $"Invalid parallelism value: {rawParallelism}";
                        return false;
                    }

                    parallelism = parsedParallelism;
                    break;

                default:
                    errorMessage = argument.StartsWith("--", StringComparison.Ordinal)
                        ? $"Unknown option: {argument}"
                        : $"Unknown argument: {argument}";
                    return false;
            }
        }

        options = new RealmFirstAchievementExportOptions(parallelism);
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

    private static bool TryParsePositiveInt(string value, out int result)
    {
        result = 0;
        return !string.IsNullOrWhiteSpace(value)
            && value.All(char.IsDigit)
            && int.TryParse(value, out result)
            && result > 0;
    }
}
