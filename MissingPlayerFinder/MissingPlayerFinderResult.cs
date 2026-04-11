namespace MissingPlayerFinder;

public sealed record MissingPlayerFinderResult(
    int SourceCharacterCount,
    int CsvCharacterCount,
    int MissingCharacterCount,
    int AppendedCharacterCount,
    int RareAchievementEntryCount,
    int RemainingMissingCharacterCount,
    string PlayersCsvPath,
    string? RareAchievementsPath,
    string? LastUpdatedPath,
    string MissingOutputPath);
