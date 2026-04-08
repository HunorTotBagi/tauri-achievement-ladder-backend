# Tauri Achievement Ladder

This repository is now a plain `.NET 9` console app. It reads the local character and guild source files, fetches fresh character details from the Tauri API, and writes `Players.csv` plus `lastUpdated.txt` to `../tauriachievements.github.io/src`.

`AchievementLadder` loads characters from:

- `AchievementLadder/Data/CharacterCollection/*.txt`
- `AchievementLadder/Data/GuildCharacters/GuildCharacters.txt`
- `RareAchiAndItemScan/Input/tauri-ban-list.txt`
- `RareAchiAndItemScan/Input/vengeful.txt`

The two `RareAchiAndItemScan/Input` files are treated as additional `Tauri` character sources.

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

On success the app prints the absolute paths for `Players.csv` and `lastUpdated.txt`.

To export guild members as `Character-Realm` rows into `GuildCharacters.txt`, run:

```bash
dotnet run --project GuildCharacterExporter
```

`GuildCharacterExporter` reuses the API settings from `AchievementLadder/appsettings.json`.

To export the `Endless` guild on `Tauri` into a real Excel workbook with one row per character plus class, race, and profession columns when the character endpoint exposes them, run:

```bash
dotnet run --project EndlessGuildExporter
```

The default workbook path is `artifacts/EndlessGuildExporter/Endless-Tauri-members.xlsx`. You can override it with `--output`.

To compare the current source inputs against `Players.csv`, fetch the missing characters from the Tauri API, append any successful lookups to `../tauriachievements.github.io/src/Players.csv`, and write any still-missing retries into `MissingPlayersToScan.txt`, run:

```bash
dotnet run --project MissingPlayerFinder
```

To scan item appearances for specific item IDs, save a JSON report, and write unresolved retry characters into `MissingItemCharactersToScan.txt`, run:

```bash
dotnet run --project MissingItemFinder -- --item-ids "73712,73709,73710"
```

To scan a single character for those items:

```bash
dotnet run --project MissingItemFinder -- --item-ids "73712,73709,73710" --name Larahh --realm Tauri
```

To rerun only the unresolved characters from the retry file, pass it back in as `--names-file`:

```bash
dotnet run --project MissingItemFinder -- --item-ids "73712,73709,73710" --names-file .\MissingItemCharactersToScan.txt
```

To scan for rare achievements, target items, and rare mounts, run:

```bash
dotnet run --project RareAchiAndItemScan
```

The default source scan already combines:

- `AchievementLadder/Data/GuildCharacters/GuildCharacters.txt`
- `RareAchiAndItemScan/Input/tauri-ban-list.txt`
- `RareAchiAndItemScan/Input/vengeful.txt`
- `AchievementLadder/Data/CharacterCollection/*.txt`

To scan just one character, pass `--name` and `--realm`:

```bash
dotnet run --project RareAchiAndItemScan -- --name Larahh --realm Tauri
```

To scan every member of one guild, pass `--guild` and `--realm`:

```bash
dotnet run --project RareAchiAndItemScan -- --guild "Outlaws" --realm Tauri
```

To scan a custom batch from a text file, pass `--names-file` and `--realm`. The file can contain one character name per line or raw lines such as `[22191791]-Fluffy|186,Mining;...`:

```bash
dotnet run --project RareAchiAndItemScan -- --names-file .\RareAchiAndItemScan\Input\tauri-ban-list.txt --realm Tauri
```

By default the scan writes a JSON report under `RareAchiAndItemScan/Output`. You can limit the scan scope with `--scan achievements`, `--scan items`, `--scan mounts`, or any comma-separated combination such as `--scan achievements,items`.

To scan the item-appearance endpoint only for specific item IDs across all default source characters, pass `--item-ids`:

```bash
dotnet run --project RareAchiAndItemScan -- --item-ids 22818,23075
```
