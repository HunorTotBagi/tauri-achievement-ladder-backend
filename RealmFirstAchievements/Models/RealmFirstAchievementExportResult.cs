namespace RealmFirstAchievements.Models;

public sealed record RealmFirstAchievementExportResult(
    string ValidCharactersPath,
    int CandidateCharacterCount,
    int ValidCharacterCount);
