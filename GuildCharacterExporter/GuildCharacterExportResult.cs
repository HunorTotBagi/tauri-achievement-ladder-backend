namespace GuildCharacterExporter;

public sealed record GuildCharacterExportResult(
    int GuildCount,
    int CharacterCount,
    int RetryGuildCount,
    string OutputPath,
    string RetryOutputPath);
