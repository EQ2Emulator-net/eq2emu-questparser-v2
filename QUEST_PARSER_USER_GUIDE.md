# EQ2Emu QuestParser User Guide

This guide covers how to use QuestParser after it is already available. It focuses on the desktop workflow, with CLI notes for repeatable or scripted use.

## What QuestParser Produces

QuestParser turns a Census quest or a manual template into reviewable EQ2Emu content files:

- `Quests/<Zone>/<quest>.quest.json` - the editable quest spec and source of truth for regeneration.
- `Quests/<Zone>/<quest>.lua` - generated quest Lua.
- `SpawnScripts/<Zone>/<quest-giver>.example.lua` - starter code for the quest giver script.
- `Quests/<Zone>/<quest>.quest.sql` - review-only SQL for quest rows, static rewards, and optional spawn script mapping.
- `Quests/<Zone>/<quest>.missing.md` - unresolved IDs, ambiguous matches, missing NPCs, and other TODOs.

The SQL is not executed by the tool. Review it before applying anything to a database.

## Recommended Workflow

1. Open the desktop app and check the settings summary strip.
2. Open `File > Settings...` if the content root, quest source, or MariaDB resolver is wrong.
3. Enter the exact quest name in `Quest name`.
4. Enter `Author` if you want the generated spec/Lua metadata to carry a name.
5. Choose either:
   - `Fetch + Resolve` for a real quest from Census, remote Census mirror, or local Census JSON.
   - `New Template` when Census data is missing or you are drafting a quest manually.
   - `Preview Spec` when you already have a `.quest.json` spec path and want to reload it.
6. Work down the `Verification Steps` sidebar.
7. For each section, compare `Quest Source and DB Data Used by This Section`, `Editable Quest Spec Values`, `DB Candidates`, `Current Lua`, and `Missing Spawn Wizard`.
8. Edit incorrect values directly in `Editable Quest Spec Values`.
9. Use `Resolve Section` after changing a query, target, reward, giver, or quest ID that should be rechecked against the DB.
10. If a section has DB candidates, select the right candidate and click `Use Selected Candidate`.
11. Click `Verify Section` only after the section looks correct.
12. Review the `Generated / Raw Data` tabs, especially `Diagnostics`, `Lua Preview`, `SQL Preview`, `Missing Report`, and `Spec JSON`.
13. Click `Generate Files` after all sections are verified and diagnostics have been reviewed.

## How to Read the Review Sections

- `Quest metadata and DB quest ID`: quest name, zone/category, level, flags, offer text, completion text, and database quest ID. A proposed quest ID is acceptable, but review the generated SQL carefully.
- `Quest giver DB reference`: the NPC that offers or completes the quest. This must resolve to a real DB reference before final use.
- `Stage text and flow`: task group text and completion text for a stage.
- `Step`: step type, display text, quantity, icon, search text, target reference, random options, or location data.
- `Quest rewards`: coin, experience, item rewards, selectable item rewards, and faction rewards.
- `Output files and final generated content`: destination paths for Lua, spec JSON, SQL, missing report, preview output, and spawn starter file.

Reference statuses mean:

- `Resolved`: the parser found a usable DB ID.
- `Proposed`: the parser proposed a usable ID, usually for a new quest row. Review before applying.
- `Ambiguous`: multiple candidates matched. Pick one with `Use Selected Candidate` or edit the query.
- `Missing`: no usable match was found. Fix the query, add the missing DB/content data, or leave it as a documented TODO.

## Diagnostics and Verification

`Generate Files` is intentionally gated. The app checks for blockers such as blank quest names, missing quest givers, ambiguous/missing target IDs, invalid quantities, missing output paths, or existing Lua files when overwrite is off.

Warnings are softer review items, such as blank zone text, generic quest steps, proposed quest IDs, or blank SQL/missing-report paths.

If blockers remain and you intentionally want to generate anyway, review the `Diagnostics` tab and check `Acknowledge blockers and allow generation`. This is useful for draft content, but final production content should usually resolve blockers instead of bypassing them.

## Settings Reference

Open `File > Settings...` for parser behavior and `View > Layout and visibility...` for workspace layout.

### Paths and Quest Source

- `Content root`: where generated `Quests` and `SpawnScripts` files are written. Change this when working against a different EQ2Emu content checkout or a staging copy.
- `Quest source`: chooses where imported quest data comes from.
- `Daybreak`: official Daybreak-compatible Census endpoint. Use this for normal online imports.
- `Remote`: hosted Census mirror. Use this when Daybreak is slow, unavailable, or you maintain a curated mirror.
- `Local`: downloaded Census-compatible JSON folder. Use this for offline work, reproducible batches, or avoiding repeated Census calls.
- `Census cache folder`: stores raw quest and questgiver JSON used by imports. Change it to keep caches outside the app folder, share a cache between parser runs, or separate live/test data.
- `Census service ID`: the `/s:...` Census service segment. Change it if you have your own Census service ID.
- `Include service ID`: enabled for normal Daybreak requests. Disable it when a remote mirror does not use a `/s:...` URL segment.
- `Daybreak base URL`: official or compatible Daybreak base URL. Leave this alone unless the endpoint changes or you are testing a compatible service.
- `Remote mirror URL`: base URL for a hosted mirror. Set this only when `Quest source` is `Remote`.
- `Local JSON folder`: folder containing Census-compatible files. QuestParser looks for quest-specific names such as `<quest>.quest.json` and `<quest>.questgivers.json`, and also accepts `quest.json`, `quests.json`, `questgiver.json`, `questgivers.json`, plus `item.json` or `items.json` for reward item names.

### Database Resolution

- `Use MariaDB`: enables automatic quest, NPC, item, spell, faction, race, and zone ID resolution. Turn it on when you want generated Lua/SQL to contain real DB IDs.
- Leave MariaDB off when drafting, working offline, or only importing Census/spec data. The parser will still generate previews, but unresolved references become TODOs.
- `Use full MariaDB connection string`: use this when you already have a complete connection string or need extra connection parameters.
- `Host`, `Port`, `Database`, `User`, `Password`: use these when a simple local or remote DB login is enough.
- `Test Connection`: verify settings before importing or resolving. Use it whenever candidates are unexpectedly missing or everything becomes unresolved.

### Layout and Visibility

- `Show or hide regions`: hide panels you do not need during focused editing. Hidden panels keep their data.
- `Sidebar width`: widen if section names are truncated; narrow if previews need more room.
- `Source panel`: adjust when source/provenance rows need more or less space.
- `Details panel`: adjust for DB candidates, Lua snippets, and missing spawn guidance.
- `Interface text`, `Tab text`, `Data text`, `Section title`: increase for readability or decrease to fit more information on screen.
- `Reset Layout`: restore default panel visibility, sizes, and text sizes.

## When to Change Common Settings

- Change `Content root` before generating into another EQ2Emu content tree.
- Switch to `Local` before large quest batches, offline work, or repeatable comparisons.
- Switch to `Remote` when you trust a mirror more than live Census for the current task.
- Disable `Include service ID` only for mirrors that fail with `/s:example` in the path.
- Enable MariaDB when you need real IDs and candidate matching.
- Disable MariaDB when you only want a draft spec or when the DB is unavailable.
- Turn on `Overwrite files` only when replacing generated Lua or spawn starter files is intentional.
- Acknowledge diagnostics only for draft output or known TODOs; for finished content, resolve the blockers.

## Manual Template Workflow

Use `New Template` when Census has no usable quest data or when authoring a brand-new quest. Templates available are `Blank`, `Speak to NPC`, `Kill NPC`, `Collect Item`, `Harvest`, `Craft`, and `Visit Location`.

After creating a template, replace every `TODO` value, resolve the giver and step targets, review the generated Lua/SQL, verify each section, and generate files. Location templates need reviewed coordinates and radius before they are production-ready.

## CLI Quick Reference

The CLI is useful for repeatable work after you know the quest/spec paths:

```powershell
questparser import --quest "A Hunter's Tool"
questparser import --quest "A Hunter's Tool" --census-source local --census-local-dir ".\downloaded-census-json"
questparser resolve --spec ".\eq2emu-content\Quests\Commonlands\a_hunters_tool.quest.json"
questparser generate --spec ".\eq2emu-content\Quests\Commonlands\a_hunters_tool.quest.json"
questparser lint --content-root ".\eq2emu-content"
```

Use `--overwrite` with `generate` only when replacing existing generated files is intentional.

## Final Review Checklist

- All verification sections are checked.
- Diagnostics have no unexpected blockers.
- Ambiguous references have been resolved to the intended candidate.
- Missing NPCs have a plan in the missing report or have been added to content/DB.
- Generated Lua uses the intended step types, quantities, and completion functions.
- Spawn starter code has been merged into the real spawn or item script as needed.
- Review SQL has been inspected before applying it to a DB.
- The `.quest.json` spec is kept with the generated content so the quest can be regenerated later.
