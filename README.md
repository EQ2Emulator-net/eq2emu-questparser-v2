# EQ2Emu QuestParser

Fresh .NET 9 quest authoring tool for EQ2Emu.

## Screenshots
<img width="1920" height="1040" alt="image" src="https://github.com/user-attachments/assets/9b1823fe-b5c9-48ac-8997-8f19d6947034" />
<img width="1920" height="1040" alt="image" src="https://github.com/user-attachments/assets/86d4f042-fbd1-48a1-ab60-954ecfe24dea" />
<img width="1920" height="1040" alt="image" src="https://github.com/user-attachments/assets/8c269517-f1c8-4655-a146-aa477d6e6afa" />


## Install

Build dependencies:

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Quest source access: Daybreak Census, a compatible remote mirror, or local downloaded JSON files
- Optional: MariaDB/MySQL access to an EQ2Emu world database for automatic ID resolution
- Linux desktop runs require a normal graphical session, such as X11 or Wayland

Release desktop executables are self-contained and do not require users to install the .NET runtime.

Setup:

```powershell
git clone <repo-url>
cd eq2emu-questparser
dotnet restore
dotnet build
```

Run the cross-platform desktop UI:

```powershell
dotnet run --project src\QuestParser.Desktop
```

Run the deprecated WinForms UI on Windows:

```powershell
dotnet run --project src\QuestParser.WinForms
```

Run the CLI:

```powershell
dotnet run --project src\QuestParser.Cli -- create --quest "A Hunter's Tool" --author "Your Name"
```

## Configuration

The tool is safe to run without MariaDB configured. Quest-source import still works, generated specs and previews are created, and DB-backed IDs are left unresolved with review TODOs. Configure DB access only when you want automatic quest, NPC, item, spell, faction, race, and zone resolution.

Defaults:

- Quest source: `daybreak`
- Census service id: `s:example`
- Daybreak Census endpoint: `https://census.daybreakgames.com`
- MariaDB: not configured by default
- Content root: `./eq2emu-content`
- Runtime cache/output/log folders: the QuestParser executable directory

Optional environment variables:

- `EQ2QP_CENSUS_SOURCE`: `daybreak`, `remote`, or `local`. Defaults to `daybreak`.
- `EQ2QP_CENSUS_SERVICE_ID`: Census service id, for example `s:example`.
- `EQ2QP_CENSUS_BASE_URL`: Daybreak-compatible endpoint. Defaults to `https://census.daybreakgames.com`.
- `EQ2QP_CENSUS_REMOTE_BASE_URL`: remote mirror endpoint when `EQ2QP_CENSUS_SOURCE=remote`.
- `EQ2QP_CENSUS_INCLUDE_SERVICE_ID`: set to `false` when a mirror does not use a `/s:...` URL segment.
- `EQ2QP_CENSUS_LOCAL_DIR`: directory containing downloaded Census-compatible JSON when `EQ2QP_CENSUS_SOURCE=local`.
- `EQ2QP_CENSUS_CACHE_DIR`: raw quest/questgiver JSON cache directory. Defaults to `cache/census` beside the executable.
- `EQ2QP_CONTENT_ROOT`: EQ2Emu content root. Defaults to `./eq2emu-content`.
- `EQ2QP_DB_CONNECTION`: full MariaDB connection string.
- `EQ2QP_DB_HOST`, `EQ2QP_DB_PORT`, `EQ2QP_DB_NAME`, `EQ2QP_DB_USER`, `EQ2QP_DB_PASSWORD`: individual MariaDB settings used when `EQ2QP_DB_CONNECTION` is not set.

PowerShell example:

```powershell
$env:EQ2QP_CONTENT_ROOT = "C:\path\to\eq2emu-content"
$env:EQ2QP_CENSUS_SOURCE = "daybreak"
$env:EQ2QP_CENSUS_SERVICE_ID = "s:example"
$env:EQ2QP_DB_HOST = "127.0.0.1"
$env:EQ2QP_DB_NAME = "eq2emu"
$env:EQ2QP_DB_USER = "eq2emu"
$env:EQ2QP_DB_PASSWORD = "<password>"
```

Remote mirror example:

```powershell
$env:EQ2QP_CENSUS_SOURCE = "remote"
$env:EQ2QP_CENSUS_REMOTE_BASE_URL = "https://your-census-mirror.example"
$env:EQ2QP_CENSUS_INCLUDE_SERVICE_ID = "false"
```

Local JSON example:

```powershell
$env:EQ2QP_CENSUS_SOURCE = "local"
$env:EQ2QP_CENSUS_LOCAL_DIR = "C:\path\to\downloaded-census-json"
```

Local mode expects Census-compatible response JSON. For a quest named `A Hunter's Tool`, the preferred file names are:

- `a_hunters_tool.quest.json`
- `a_hunters_tool.questgivers.json`

It also accepts `quest.json`/`quests.json` and `questgiver.json`/`questgivers.json` for single-dataset folders.

Download a local EQ2 Census snapshot for local mode:

```powershell
.\scripts\Download-Eq2CensusLocalData.ps1 -RequestDelaySeconds 10
$env:EQ2QP_CENSUS_SOURCE = "local"
$env:EQ2QP_CENSUS_LOCAL_DIR = ".\artifacts\census\eq2"
```

The downloader writes parser-ready `quest.json`, `questgiver.json`, and `questgivers.json`, plus raw Census page responses under `raw\`. By default it fetches `quest`, `questgiver`, `npc`, `faction`, `zone`, and `world`; pass `-Collections quest,questgiver` for the smallest parser-only snapshot or add other EQ2 collection names as needed. Quest and questgiver use Census `c:show` field selection by default so large pages can be fetched reliably; pass `-NoFieldSelection` for slower unfiltered 100-row Census batches.

## Commands

UI:

```powershell
dotnet run --project src\QuestParser.Desktop
dotnet run --project src\QuestParser.WinForms
```

`QuestParser.Desktop` is the Avalonia UI for Windows, macOS, and Linux. It imports or creates quests,
previews generated Lua/SQL/missing reports/spec JSON, and can generate files. The Quest Source panel can switch
between Daybreak, a compatible remote mirror, and a local JSON folder before importing.

`QuestParser.WinForms` is deprecated and kept only as the older Windows-only UI. It fetches a quest by name, resolves DB references, then shows the imported quest-source data, DB candidates,
editable quest/spec fields, generated Lua, review SQL, missing-data report, and raw cached Census JSON.
Each generated section must be manually verified before files can be written. Existing Lua files are still
protected unless `Overwrite Lua` is checked.

Additional UI authoring helpers:

- `Resolve Section` re-runs DB resolution only for the current quest ID, giver, step, or reward section.
- The review grid includes provenance so values can be traced to the quest source, DB resolution, generated defaults, templates, or user overrides.
- Missing NPC references show a Missing Spawn Wizard with suggested spawn script path, Lua TODO text, and commented review-only SQL.
- The Diagnostics tab lists blockers and warnings. Blockers must be fixed or explicitly acknowledged before generation.
- `New Template` creates manual drafts for blank, speak-to-NPC, kill, collect-item, harvest, craft, and visit-location quests when Census data is incomplete.

CLI:

```powershell
dotnet run --project src\QuestParser.Cli -- create --quest "A Hunter's Tool" --author "Your Name"
dotnet run --project src\QuestParser.Cli -- import --quest "A Hunter's Tool"
dotnet run --project src\QuestParser.Cli -- import --quest "A Hunter's Tool" --census-source local --census-local-dir ".\downloaded-census-json"
dotnet run --project src\QuestParser.Cli -- resolve --spec ".\eq2emu-content\Quests\Commonlands\a_hunters_tool.quest.json"
dotnet run --project src\QuestParser.Cli -- generate --spec ".\eq2emu-content\Quests\Commonlands\a_hunters_tool.quest.json"
dotnet run --project src\QuestParser.Cli -- lint
```

Existing Lua files are protected by default. Use `--overwrite` with `create` or `generate` when replacement is intentional.

Generated files:

- `Quests\<Zone>\<quest>.quest.json`
- `Quests\<Zone>\<quest>.lua`
- `Quests\<Zone>\<quest>.quest.sql`
- `Quests\<Zone>\<quest>.missing.md`

The SQL file is review-only. The tool resolves from the DB but does not insert, update, or delete database rows.
Static quest rewards are emitted as `quest_details` SQL rows instead of Lua reward calls, so applying both generated Lua and generated SQL will not duplicate rewards.

## Verify

```powershell
dotnet build
dotnet test
```

## Publish

`QuestParser.Desktop` is the release entry point. One executable cannot run on Windows, Linux, and macOS, so publish one self-contained single-file executable per OS/CPU runtime. Release publishes are trimmed, compressed, self-contained, and remove debug symbols from the publish folder.

```powershell
dotnet publish src\QuestParser.Desktop -c Release -r win-x64 -o artifacts\release\win-x64
dotnet publish src\QuestParser.Desktop -c Release -r linux-x64 -o artifacts\release\linux-x64
dotnet publish src\QuestParser.Desktop -c Release -r osx-arm64 -o artifacts\release\osx-arm64
dotnet publish src\QuestParser.Desktop -c Release -r osx-x64 -o artifacts\release\osx-x64
```

Publish output:

- Windows: `artifacts\release\win-x64\eq2emu-questparser.exe`
- Linux: `artifacts\release\linux-x64\eq2emu-questparser`
- macOS Apple Silicon: `artifacts\release\osx-arm64\eq2emu-questparser`
- macOS Intel: `artifacts\release\osx-x64\eq2emu-questparser`

Use `win-arm64` or `linux-arm64` if you want ARM64 releases for those platforms.

For Linux/macOS downloads, users may need to run:

```bash
chmod +x eq2emu-questparser
```
