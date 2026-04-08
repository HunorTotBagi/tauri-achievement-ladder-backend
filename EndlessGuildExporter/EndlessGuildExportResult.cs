namespace EndlessGuildExporter;

public sealed record EndlessGuildExportResult(
    int CharacterCount,
    int CharacterSheetCount,
    int FallbackCount,
    string OutputPath);
