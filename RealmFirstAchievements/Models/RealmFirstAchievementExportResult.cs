namespace RealmFirstAchievements.Models;

public sealed record RealmFirstAchievementExportResult(
    string FrontendValidCharactersPath,
    int CandidateCharacterCount,
    int ValidCharacterCount);
