# Tauri Achievement Ladder

This repository is now a plain `.NET 9` console app. It reads the local character and guild source files, fetches fresh character details from the Tauri API, and writes `Players.csv` to the repository root.

There is no database, no EF Core migration flow, and no Docker setup anymore.

## Configuration

Set real Tauri API credentials in `AchievementLadder/appsettings.json` or override them with environment variables:

- `TAURI_API_BASEURL`
- `TAURI_API_APIKEY`
- `TAURI_API_SECRET`

## Run

```bash
dotnet run --project AchievementLadder
```

On success the app prints the absolute path for `Players.csv`.

To export guild members as `Character-Realm` rows into `GuildCharacters.txt`, run:

```bash
dotnet run --project GuildCharacterExporter
```

`GuildCharacterExporter` reuses the API settings from `AchievementLadder/appsettings.json`.
