namespace MissingPlayerFinder;

public sealed record MissingPlayerFinderResult(
    int SourceCharacterCount,
    int CsvCharacterCount,
    int MissingCharacterCount,
    int AppendedCharacterCount,
    int RemainingMissingCharacterCount,
    string PlayersCsvPath,
    string? LastUpdatedPath,
    string MissingOutputPath);
