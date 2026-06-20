namespace GuildNameBruteForcer;

internal enum GuildScanMode
{
    BruteForce,
    Dictionary
}

internal enum DictionaryLanguage
{
    Hungarian,
    English
}

internal sealed record GuildScanOptions(
    GuildScanMode Mode,
    int? NameLength,
    string? RealmFilter,
    string? DictionaryPath,
    DictionaryLanguage DictionaryLanguage,
    int MinLength,
    int MaxLength,
    bool CountOnly)
{
    public const string UsageText = """
        Usage:
          GuildNameBruteForcer <length> [realm]
          GuildNameBruteForcer dictionary [options]

        Brute-force mode:
          length                 Number of characters to brute-force (1-10).
          realm                  evermoon | tauri | wod (default: all).

        Dictionary mode:
          --language <language>  hu | en (default: hu).
          --realm <realm>        evermoon | tauri | wod | all (default: tauri).
                                 Without --realm, English defaults to evermoon.
          --file <path>          Hunspell .dic or plain UTF-8 word list.
                                 Defaults to the selected language and downloads it if missing.
          --min-length <number>  Shortest candidate to scan (default: 1).
          --max-length <number>  Longest candidate to scan (default: 32).
          --count-only           Parse and count candidates without calling the API.

        Examples:
          dotnet run --project GuildNameBruteForcer -- 4 tauri
          dotnet run --project GuildNameBruteForcer -- dictionary
          dotnet run --project GuildNameBruteForcer -- dictionary --language en
          dotnet run --project GuildNameBruteForcer -- dictionary --language hu --realm wod
          dotnet run --project GuildNameBruteForcer -- dictionary --file .\Input\my-words.txt --count-only
        """;

    public static bool TryParse(
        string[] args,
        out GuildScanOptions? options,
        out string? errorMessage,
        out bool showHelp)
    {
        options = null;
        errorMessage = null;
        showHelp = args.Length > 0 && args[0] is "--help" or "-h" or "help";

        if (showHelp)
        {
            return true;
        }

        if (args.Length == 0)
        {
            errorMessage = "A brute-force length or the 'dictionary' command is required.";
            return false;
        }

        if (int.TryParse(args[0], out var nameLength))
        {
            return TryParseBruteForce(args, nameLength, out options, out errorMessage);
        }

        if (!args[0].Equals("dictionary", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = $"Unknown command '{args[0]}'.";
            return false;
        }

        return TryParseDictionary(args, out options, out errorMessage);
    }

    private static bool TryParseBruteForce(
        string[] args,
        int nameLength,
        out GuildScanOptions? options,
        out string? errorMessage)
    {
        options = null;
        errorMessage = null;

        if (nameLength is < 1 or > 10)
        {
            errorMessage = "Brute-force length must be between 1 and 10.";
            return false;
        }

        if (args.Length > 2)
        {
            errorMessage = "Brute-force mode accepts only <length> and an optional [realm].";
            return false;
        }

        var realmFilter = args.Length == 2 ? NormalizeRealm(args[1], allowAll: false) : null;
        if (args.Length == 2 && realmFilter is null)
        {
            errorMessage = $"Unknown realm '{args[1]}'. Valid values: evermoon, tauri, wod.";
            return false;
        }

        options = new GuildScanOptions(
            GuildScanMode.BruteForce,
            nameLength,
            realmFilter,
            null,
            DictionaryLanguage.Hungarian,
            nameLength,
            nameLength,
            CountOnly: false);
        return true;
    }

    private static bool TryParseDictionary(
        string[] args,
        out GuildScanOptions? options,
        out string? errorMessage)
    {
        options = null;
        errorMessage = null;

        string? dictionaryPath = null;
        string? realmFilter = null;
        var realmWasSpecified = false;
        var dictionaryLanguage = DictionaryLanguage.Hungarian;
        var minLength = 1;
        var maxLength = 32;
        var countOnly = false;

        for (var index = 1; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument.ToLowerInvariant())
            {
                case "--language":
                    if (!TryReadValue(args, ref index, argument, out var languageValue, out errorMessage))
                    {
                        return false;
                    }

                    if (!TryParseLanguage(languageValue!, out dictionaryLanguage))
                    {
                        errorMessage = $"Unknown language '{languageValue}'. Valid values: hu, en.";
                        return false;
                    }
                    break;

                case "--realm":
                    if (!TryReadValue(args, ref index, argument, out var realmValue, out errorMessage))
                    {
                        return false;
                    }

                    realmWasSpecified = true;
                    realmFilter = NormalizeRealm(realmValue!, allowAll: true);
                    if (realmFilter is null && !realmValue!.Equals("all", StringComparison.OrdinalIgnoreCase))
                    {
                        errorMessage = $"Unknown realm '{realmValue}'. Valid values: evermoon, tauri, wod, all.";
                        return false;
                    }
                    break;

                case "--file":
                    if (!TryReadValue(args, ref index, argument, out dictionaryPath, out errorMessage))
                    {
                        return false;
                    }
                    break;

                case "--min-length":
                    if (!TryReadPositiveInt(args, ref index, argument, out minLength, out errorMessage))
                    {
                        return false;
                    }
                    break;

                case "--max-length":
                    if (!TryReadPositiveInt(args, ref index, argument, out maxLength, out errorMessage))
                    {
                        return false;
                    }
                    break;

                case "--count-only":
                    countOnly = true;
                    break;

                default:
                    errorMessage = $"Unknown dictionary option '{argument}'.";
                    return false;
            }
        }

        if (minLength > maxLength)
        {
            errorMessage = "--min-length cannot be greater than --max-length.";
            return false;
        }

        if (!realmWasSpecified)
        {
            realmFilter = dictionaryLanguage == DictionaryLanguage.Hungarian ? "tauri" : "evermoon";
        }

        options = new GuildScanOptions(
            GuildScanMode.Dictionary,
            null,
            realmFilter,
            dictionaryPath,
            dictionaryLanguage,
            minLength,
            maxLength,
            countOnly);
        return true;
    }

    private static bool TryParseLanguage(string rawLanguage, out DictionaryLanguage language)
    {
        switch (rawLanguage.Trim().ToLowerInvariant())
        {
            case "hu":
            case "hungarian":
                language = DictionaryLanguage.Hungarian;
                return true;

            case "en":
            case "english":
                language = DictionaryLanguage.English;
                return true;

            default:
                language = default;
                return false;
        }
    }

    private static string? NormalizeRealm(string rawRealm, bool allowAll)
    {
        var realm = rawRealm.Trim().ToLowerInvariant();
        if (allowAll && realm == "all")
        {
            return null;
        }

        return realm is "evermoon" or "tauri" or "wod" ? realm : null;
    }

    private static bool TryReadValue(
        string[] args,
        ref int index,
        string option,
        out string? value,
        out string? errorMessage)
    {
        value = null;
        errorMessage = null;

        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            errorMessage = $"{option} requires a value.";
            return false;
        }

        value = args[++index].Trim();
        if (value.Length == 0)
        {
            errorMessage = $"{option} requires a non-empty value.";
            return false;
        }

        return true;
    }

    private static bool TryReadPositiveInt(
        string[] args,
        ref int index,
        string option,
        out int value,
        out string? errorMessage)
    {
        value = 0;
        if (!TryReadValue(args, ref index, option, out var rawValue, out errorMessage))
        {
            return false;
        }

        if (!int.TryParse(rawValue, out value) || value <= 0)
        {
            errorMessage = $"{option} must be a positive whole number.";
            return false;
        }

        return true;
    }
}
