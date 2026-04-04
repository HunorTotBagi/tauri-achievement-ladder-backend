namespace MissingItemFinder;

public sealed record MissingItemFinderResult(
    int SourceCharacterCount,
    int ScannedCharacterCount,
    int MatchedCharacterCount,
    int RemainingCharacterCount,
    int ItemMatchCount,
    string OutputPath,
    string MissingOutputPath);

public sealed record MissingItemFinderReport(
    DateTimeOffset GeneratedAtUtc,
    string Scope,
    IReadOnlyList<int> ItemIds,
    int ScannedCharacterCount,
    int FailedCharacterCount,
    IReadOnlyList<string> Failures,
    IReadOnlyList<ItemCharacterScanResult> Characters);

public sealed record ItemCharacterScanResult(
    string Name,
    string Realm,
    IReadOnlyList<ItemMatchResult> Items);

public sealed record ItemMatchResult(
    int ItemId,
    string Name);
