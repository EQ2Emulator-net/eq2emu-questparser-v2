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

Create local GitHub release upload artifacts:

```powershell
.\scripts\Create-GitHubRelease.ps1
```

This writes the platform archives to the gitignored `github-release` folder. GitHub adds the `Source code (zip)` and `Source code (tar.gz)` rows automatically from the release tag. On Windows, double-click `Create-GitHubRelease.cmd` from the repo root for the same release build.

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

The downloader writes parser-ready `quest.json`, `questgiver.json`, `questgivers.json`, and `item.json`, plus raw Census page responses under `raw\`. By default it fetches `quest`, `questgiver`, `item`, `npc`, `faction`, `zone`, and `world`; pass `-Collections quest,questgiver,item` for the smallest reward-aware parser snapshot or add other EQ2 collection names as needed. Quest, questgiver, and item use Census `c:show` field selection by default so large pages can be fetched reliably; pass `-NoFieldSelection` for slower unfiltered 100-row Census batches.

## Commands

UI:

```powershell
dotnet run --project src\QuestParser.Desktop
dotnet run --project src\QuestParser.Desktop -- --visual-editor
dotnet run --project src\QuestParser.Desktop -- --visual-editor --spec ".\eq2emu-content\Quests\Commonlands\a_hunters_tool.quest.json"
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
- `File > Settings...` includes `Lua generation`, which switches between the current legacy quest Lua/spawn-starter output and the newer shared `QuestModule` Lua output.
- `File > Open Visual Editor...` opens the loaded quest spec in a larger graph editor window.
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
dotnet run --project src\QuestParser.Cli -- generate --spec ".\eq2emu-content\Quests\Commonlands\a_hunters_tool.quest.json" --mode module-lua --overwrite
dotnet run --project src\QuestParser.Cli -- lint
```

Existing generated files are protected by default. Use `--overwrite` with `create` or `generate` when replacement is intentional.

Generated files:

- `Quests\<Zone>\<quest>.quest.json`
- `Quests\<Zone>\<quest>.lua`
- `SpawnScripts\<Zone>\<quest-giver>.example.lua`
- `Quests\<Zone>\<quest>.quest.sql`
- `Quests\<Zone>\<quest>.missing.md`

The spawn script is an example starter scaffold for the resolved quest giver. Merge the relevant hail, click/cast, use, or item-examine hook into the live spawn or item script after review.

## Visual Editor

The visual editor is a graph-first editor for the same `.quest.json` spec used by the normal QuestParser review workflow. The `.quest.json` file remains the single source of truth; graph layout is stored inside that file under `visualEditor`, and there is no separate graph project file.

Open it from the desktop UI:

1. Load, import, or create a quest in `QuestParser.Desktop`.
2. Use `File > Open Visual Editor...`.
3. Edit the graph in the popup window.
4. Click `Save` to return the changed spec to the main review window.
5. Review diagnostics and generate files from the main window.

Open it as a standalone editor:

```powershell
dotnet run --project src\QuestParser.Desktop -- --visual-editor
dotnet run --project src\QuestParser.Desktop -- --visual-editor --spec ".\eq2emu-content\Quests\Commonlands\a_hunters_tool.quest.json"
```

Standalone mode can open a spec, save the spec, validate it, preview Lua/SQL/missing output, and generate files directly. When the visual editor is opened from the main QuestParser window, generation from the popup is disabled until the edited spec is saved back to the main window.

Graph editing:

- Double-click an item in the `Actions` palette to add a quest step to the selected stage. Supported action types match the quest parser step types: generic, chat, kill, kill self update, kill by race, obtain item, spell, craft, harvest, location, and zone location.
- Double-click `Stage` or `Parallel Stage` in the `Flow` palette to add a stage. `Random Options` and `Comment` are visible placeholders and are not wired yet.
- Select a node to edit it in the inspector. Stage text, completed text, parallel yes/no, step description, completed text, target search text, quantity, and step stage assignment are editable there. System fields such as selected node metadata and node kind are read-only.
- Drag graph nodes to adjust layout. Layout changes are saved under `visualEditor` in the spec.
- Use `Edit connections`, then select a source node and a target node. Stage-to-stage connections reorder stages. Step-to-stage connections move a step to the end of the target stage. Step-to-step connections move a step after the target step.
- Select a stage or step and use the `Delete` button, the `Delete` key, or `Backspace` to remove it. Start, complete, and generated join nodes cannot be deleted.
- Use `Undo` and `Redo` for graph, inspector, delete, and connection edits.
- Use `Zoom in`, `Zoom out`, and `Center` when working with large quests or wide parallel stages.
- Use the bottom tabs for diagnostics, walkthrough text, Lua preview, SQL preview, missing-data report, and generation log.
- Use `Definition` to inspect the generated graph definition for the current workflow or selected node.

The visual editor intentionally stays within what QuestParser can already generate. It edits stages, parser-supported step types, parallel stage flags, target search text, quantities, and layout. Advanced manual Lua behavior should still be added after generation or by extending the `.quest.json` model and generators.

## QuestModule Lua

QuestParser has two Lua generation modes:

- `legacy-spawn-stub` is the default and preserves the current generated quest Lua plus spawn-starter workflow.
- `module-lua` emits quest Lua that delegates step setup/reload/completion boilerplate to `Quests/Generic/QuestModule.lua`.
- CLI `create`/`generate --mode module-lua` uses strict module validation and will not write output when module-specific blockers such as duplicate step IDs, non-contiguous stages, or invalid quantity ranges are present.

Use QuestModule mode from the desktop UI:

1. Open `File > Settings...`.
2. Set `Lua generation` to `Quest module Lua`.
3. Confirm `Content root` points at the EQ2Emu content repository you will run on the server.
4. In the `QuestModule` row, click `Copy` when the module is missing or outdated.
5. Save settings, review diagnostics, then generate files normally.

The settings window checks `Quests/Generic/QuestModule.lua` by SHA-256 hash against the QuestParser-bundled module. If it is missing, the copy button creates it. If it is outdated, the button updates it. The generated quest Lua uses:

```lua
require "Quests/Generic/QuestModule"
```

Use QuestModule mode from the CLI:

```powershell
dotnet run --project src\QuestParser.Cli -- create --quest "A Hunter's Tool" --author "Your Name" --mode module-lua --overwrite
dotnet run --project src\QuestParser.Cli -- generate --spec ".\eq2emu-content\Quests\Commonlands\a_hunters_tool.quest.json" --mode module-lua --overwrite
```

The CLI does not copy the module for you. If `Quests/Generic/QuestModule.lua` is missing or does not match the bundled version, diagnostics include `MODULE_LUA_MISSING_QUEST_MODULE` or `MODULE_LUA_OUTDATED_QUEST_MODULE`.

Generated QuestModule Lua is organized around stage step tables:

```lua
local STAGE_1_STEPS = {}
local STAGE_2_STEPS = {}

local ALL_STEPS = QuestModule.ExportStageStepHandlers({
    STAGE_1_STEPS,
    STAGE_2_STEPS,
}, { overwrite = true })
```

You should not need to manually call `QuestModule.ExportStepHandlers` for each stage or manually append every stage into `ALL_STEPS`. The generator calls `QuestModule.ExportStageStepHandlers`, which exports all step completion callbacks and returns the combined ordered step list used by `Reload`.

QuestModule handles:

- `QuestModule.AddSteps(Quest, steps)` for adding a stage's steps.
- `QuestModule.ReloadByStep(Quest, QuestGiver, Player, Step, nil, ALL_STEPS)` for reload routing.
- `QuestModule.AllComplete(Player, questId, steps)` for parallel stages where all steps must be complete before advancing.
- `QuestModule.OnAllComplete(...)` for custom manual all-complete checks.
- `QuestModule.CompleteQuest(...)` for common completion description and reward handling.
- `QuestModule.BuildNamedSteps(...)` for hand-authored step tables that still need validation and contiguous numeric IDs.

Parallel stages generated in `module-lua` use `QuestModule.AllComplete` instead of writing one `QuestStepIsComplete` check per step. Each parallel step calls the stage progress handler, and the stage advances only when all step IDs in that stage table are complete.

QuestModule supports the parser's generated step types: `basic`, `chat`, `kill`, `killSelfUpdate`, `killByRace`, `obtainItem`, `spell`, `craft`, `harvest`, `location`, and `zoneLoc`. It also validates callback names, target arrays, location data, random option data, and quantity ranges at load/runtime.

Developer workflow for generated module Lua:

1. Keep generated step tables and generated callback names intact unless you are intentionally hand-editing the quest.
2. Resolve any generated `TODO DB` comments by fixing the `.quest.json`, DB data, or final Lua targets.
3. Put custom quest-only behavior in the generated stage completion functions, `QuestComplete`, or explicitly manual callbacks.
4. Keep shared step boilerplate in `Quests/Generic/QuestModule.lua` so generated quests stay consistent.

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
