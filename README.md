# EQ2Emu QuestParser

Fresh .NET 9 quest authoring tool for EQ2Emu.

## Defaults

- Census service id: `s:example`
- Census endpoint: `https://census.daybreakgames.com`
- MariaDB: not configured by default
- Content root: `./eq2emu-content`
- Runtime cache/output/log folders: the QuestParser executable directory

## Configuration

The tool is safe to run without MariaDB configured. Census import still works, generated specs and previews are created, and DB-backed IDs are left unresolved with review TODOs. Configure DB access only when you want automatic quest, NPC, item, spell, faction, race, and zone resolution.

Environment variables:

- `EQ2QP_CENSUS_SERVICE_ID`: Census service id, for example `s:example`.
- `EQ2QP_CENSUS_BASE_URL`: Census endpoint. Defaults to `https://census.daybreakgames.com`.
- `EQ2QP_CONTENT_ROOT`: EQ2Emu content root. Defaults to `./eq2emu-content`.
- `EQ2QP_DB_CONNECTION`: full MariaDB connection string.
- `EQ2QP_DB_HOST`, `EQ2QP_DB_PORT`, `EQ2QP_DB_NAME`, `EQ2QP_DB_USER`, `EQ2QP_DB_PASSWORD`: individual MariaDB settings used when `EQ2QP_DB_CONNECTION` is not set.

## Commands

UI:

```powershell
dotnet run --project src\QuestParser.WinForms
```

The UI fetches a quest by name, resolves DB references, then shows the imported Census data, DB candidates,
editable quest/spec fields, generated Lua, review SQL, missing-data report, and raw cached Census JSON.
Each generated section must be manually verified before files can be written. Existing Lua files are still
protected unless `Overwrite Lua` is checked.

Additional UI authoring helpers:

- `Resolve Section` re-runs DB resolution only for the current quest ID, giver, step, or reward section.
- The review grid includes provenance so values can be traced to Census, DB resolution, generated defaults, templates, or user overrides.
- Missing NPC references show a Missing Spawn Wizard with suggested spawn script path, Lua TODO text, and commented review-only SQL.
- The Diagnostics tab lists blockers and warnings. Blockers must be fixed or explicitly acknowledged before generation.
- `New Template` creates manual drafts for blank, speak-to-NPC, kill, collect-item, harvest, craft, and visit-location quests when Census data is incomplete.

CLI:

```powershell
dotnet run --project src\QuestParser.Cli -- create --quest "A Hunter's Tool" --author "Your Name"
dotnet run --project src\QuestParser.Cli -- import --quest "A Hunter's Tool"
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
