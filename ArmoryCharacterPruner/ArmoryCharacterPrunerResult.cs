namespace ArmoryCharacterPruner;

public sealed record ArmoryCharacterPrunerResult(
    string InputPath,
    string OutputPath,
    int TotalLineCount,
    int CheckedCharacterRowCount,
    int UniqueCharacterCount,
    int RemovedRowCount,
    int KeptRowCount,
    int UnparsedRowCount,
    int FailedLookupCount);
