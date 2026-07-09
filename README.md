# Tauri Achievement Ladder

This repository is now a plain `.NET 9` console app. It reads the local character and guild source files, fetches fresh character details from the Tauri API, and writes `Players.csv`, `RareAchievements.json`, plus `lastUpdated.txt` to `../tauriachievements.github.io/src`.

`AchievementLadder` loads characters from:

- `AchievementLadder/Data/CharacterCollection/*.txt`
- `AchievementLadder/Data/GuildCharacters/GuildCharacters.txt`
- `AchievementLadder/Data/PvPSeasonCharacters/*.txt`
- `AchievementLadder/Data/AdditionalCharacters/tauri-ban-list.txt`
- `AchievementLadder/Data/AdditionalCharacters/vengeful.txt`

The two `AchievementLadder/Data/AdditionalCharacters` files are treated as additional `Tauri` character sources.

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

On success the app prints the absolute paths for `Players.csv`, `RareAchievements.json`, and `lastUpdated.txt`.

`RareAchievements.json` contains:

- the exported rare-achievement catalog from `RareScanCatalog.RareAchievementNames`
- one entry per exported character with `name`, `realm`, and the matching rare `achievementIds`

To export guild members as `Character-Realm` rows into `GuildCharacters.txt`, run:

```bash
dotnet run --project GuildCharacterExporter
```

`GuildCharacterExporter` reuses the API settings from `AchievementLadder/appsettings.json`.
If `MissingGuildsToScan.txt` contains retry rows from a previous run, the next run scans only those guilds and merges successful retry members into the existing `GuildCharacters.txt`.

To export the `Endless` guild on `Tauri` into a real Excel workbook with one row per character plus class, race, and profession columns when the character endpoint exposes them, run:

```bash
dotnet run --project EndlessGuildExporter
```

The default workbook path is `EndlessGuildExporter/Endless-Legion-Roster.xlsx`. You can override it with `--output`; relative output paths are resolved from the `EndlessGuildExporter` project root.

To compare the current source inputs against `Players.csv`, fetch the missing characters from the Tauri API, append any successful lookups to `../tauriachievements.github.io/src/Players.csv`, and write any still-missing retries into `MissingPlayersToScan.txt`, run:

```bash
dotnet run --project MissingPlayerFinder
```

To rebuild the validated realm-first character source file, run:

```bash
dotnet run --project RealmFirstAchievements
```

You can raise or lower this run's API request parallelism with `--parallelism`:

```bash
dotnet run --project RealmFirstAchievements -- --parallelism 30
```

To collect battleground metadata from consecutive `pvp-match` ids into JSON, seed the first run with a known match id:

```bash
dotnet run --project BattlegroundCollector -- 95874
```

After that, run it without arguments. It resumes from `../tauriachievements.github.io/src/battleground-collector-state.json`, stops at the first missing match response, and prepends newly found battlegrounds to `../tauriachievements.github.io/src/battlegrounds.json`.
