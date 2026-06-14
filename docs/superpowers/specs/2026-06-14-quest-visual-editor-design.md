# Quest Visual Editor Design

## Context

EQ2Emu QuestParser is currently a .NET 9 solution with reusable generation logic in `QuestParser.Core`, a release Avalonia desktop UI in `QuestParser.Desktop`, a deprecated WinForms UI, a CLI, and xUnit tests. The local branch already includes the newer `QuestModule` Lua generation mode, so this design targets the current local architecture rather than older public code references.

The existing `.quest.json` `QuestSpec` file is the canonical source for regeneration. It already stores quest metadata, stages, steps, rewards, resolver state, output paths, provenance, generation mode, and generation status. The visual editor must preserve that contract.

## Product Goal

Add a visual quest editor for authoring and reviewing QuestParser quests. The editor must work with equal priority in two modes:

- Standalone visual editor entry point.
- Integrated QuestParser flow, opened from `QuestParser.Desktop` as a large separate editor window.

The editor should be inspired by AWS Step Functions: a searchable left palette, dotted workflow canvas, compact action cards, explicit start/end nodes, toolbar controls, and a right inspector with `Form` and `Definition` views.

## Current Source Adjustments To The Attached Requirements

The attached requirements remain directionally useful, but these adjustments are required for the local source:

- Target `QuestParser.Desktop` first. It is the current Avalonia UI; WinForms is deprecated.
- Keep `.quest.json` as the only quest-data file. Do not introduce `.eq2questgraph.json` as a second source of truth.
- Add visual metadata inside `QuestSpec` under a versioned editor section.
- Preserve all local `StepType` values: `Generic`, `Chat`, `Craft`, `Harvest`, `Kill`, `KillByRace`, `Location`, `ObtainItem`, `Spell`, and `ZoneLocation`.
- Preserve existing stage behavior: sequential stages, parallel stages, and random-option kill/kill-by-race stages.
- Generate only through the existing `QuestWorkflow.Preview` and `QuestWorkflow.GenerateFromSpecAsync` paths.
- Support both `LegacySpawnStub` and `ModuleLua` generation modes.
- Keep existing CLI behavior and existing parser review/generation workflows intact.

## Approved Approach

Use a spec-first graph overlay.

`QuestSpec` remains the canonical quest model. The graph is a projection of `QuestSpec.Stages` and `QuestStepSpec` for editing and visualization. Graph edits are linearized back into `QuestSpec` before validation, preview, or generation. The visual graph must not introduce arbitrary flow semantics that the existing generators cannot represent.

Rejected alternatives:

- Graph-first canonical model: more flexible later, but creates a second quest representation and increases parity risk.
- UI-only visualization: safest technically, but does not meet the visual authoring goal.

## Architecture

Add reusable graph services to `QuestParser.Core`:

- `QuestVisualEditorState`: versioned visual editor metadata stored on `QuestSpec`.
- `QuestGraphNodeLayout`: stable node ID, node kind, related stage/step identifiers, position, size, collapsed state, and review state.
- `QuestGraphViewport`: canvas pan and zoom.
- `QuestGraphProjector`: converts `QuestSpec` into a visual graph session.
- `QuestGraphLinearizer`: applies graph edits back to ordered `QuestSpec.Stages` and `QuestStepSpec` objects.
- `QuestGraphValidator`: validates graph shape against current QuestParser semantics.
- `QuestGraphLayoutService`: creates and repairs AWS-Step-Functions-style top-down layout.

Add Avalonia editor UI in `QuestParser.Desktop`:

- `VisualEditorWindow`: full editing workspace.
- `VisualEditorViewModel`: owns the graph session, selected item, dirty state, validation rows, and preview state.
- `QuestGraphCanvas`: custom Avalonia canvas/control for nodes, edges, pan, zoom, selection, drag, and auto-layout.
- Inspector/editor components for quest, stage, step, rewards, and output fields.

Generation remains in existing services:

- `QuestWorkflow.Preview`
- `QuestSpecValidator.Validate`
- `QuestWorkflow.GenerateFromSpecAsync`
- `LuaGenerator`
- `ModuleLuaGenerator`
- `SqlReportGenerator`
- `SpawnScriptGenerator`

## Data Model

Extend `QuestSpec` with a nullable visual editor section:

```json
{
  "schemaVersion": "1.0",
  "generationMode": "ModuleLua",
  "quest": {},
  "stages": [],
  "visualEditor": {
    "schemaVersion": 1,
    "layoutVersion": 1,
    "viewport": { "x": 0, "y": 0, "zoom": 1.0 },
    "nodes": [
      {
        "id": "stage-1-step-2",
        "kind": "Step",
        "stageNumber": 1,
        "stepNumber": 2,
        "x": 420,
        "y": 260,
        "width": 260,
        "height": 72,
        "collapsed": false,
        "reviewStatus": "NeedsReview"
      }
    ]
  }
}
```

The visual editor section stores layout and editor state only. Quest data remains in existing `QuestSpec` fields.

Stable layout matching should prefer explicit visual node IDs. When stage or step numbers change, the layout repair process may fall back to stage/step number, description, and type to preserve positions where practical.

## Supported Graph Semantics

MVP supports exactly the semantics QuestParser can already generate:

- One start node.
- One complete/end node.
- Ordered sequential stages.
- Parallel stages, where all steps in a stage must complete before the next stage is added.
- Random-option kill/kill-by-race stages, represented as one step card with nested option rows.
- Existing ten step types.

MVP does not support arbitrary Choice nodes, custom conditional branching, loops, failure paths, class/race branches, or merge nodes. Unsupported flow ideas can be represented later only after the generation model supports them.

## UI Layout

The visual editor window uses four primary regions.

Toolbar:

- Undo
- Redo
- Zoom in
- Zoom out
- Center
- Auto layout
- Validate
- Preview
- Generate
- Save
- Form/Definition toggle

Left pane:

- Search box.
- `Actions` tab: Chat, Kill, Kill by Race, Obtain Item, Harvest, Craft, Location, Zone Location, Spell, Generic.
- `Flow` tab: Stage, Parallel Stage, Random Options, Comment.
- Start and Complete are locked generated nodes shown on every graph, not palette items that can be duplicated.

Center canvas:

- Dotted grid.
- Top-down workflow.
- Circular Start and Complete nodes.
- Compact step cards with type strip/icon, type label, description, quantity, status badges, and missing-data markers.
- Parallel stages drawn as fan-out/fan-in groups.
- Random options shown inside a single generated step card.
- Pan, zoom, drag, select, delete, reorder, auto-layout, and center.

Right pane:

- `Form` view for normal editing.
- `Definition` view for JSON/spec-oriented details.
- Selected item determines the editor: quest, stage, step, random option, reward, output, or graph canvas.

Bottom panel:

- Diagnostics.
- Walkthrough.
- Lua preview.
- SQL preview.
- Missing report.
- Generation log.

## Workflows

Standalone launch:

1. Open visual editor directly.
2. Create from template, import from Census, or open an existing `.quest.json`.
3. Resolve references when configured.
4. Edit visually.
5. Validate.
6. Preview Lua/SQL/missing report.
7. Generate files through existing QuestWorkflow.
8. Save the `.quest.json`.

Integrated launch:

1. User imports, creates, or previews a spec in `QuestParser.Desktop`.
2. User clicks `Open Visual Editor`.
3. A separate editor window opens with the current `QuestSpec`.
4. User edits and saves.
5. Updated spec returns to the main parser review window.
6. Existing section review and generation remain available.

CLI:

- Existing CLI behavior remains unchanged.
- Visual metadata in `.quest.json` is preserved by normal spec read/write.
- A CLI switch that opens the standalone visual editor with a spec path is out of scope for MVP.

## Validation

Validation has two layers.

Graph validation blocks:

- Missing start or complete node.
- More than one generated start or complete node.
- Disconnected generated stage or step.
- Unsupported branching.
- Cycles.
- Parallel stage without a valid join.
- Random-option node that cannot map to the current random option representation.
- Duplicate generated stage or step identity after linearization.

Existing spec validation blocks or warns using `QuestSpecValidator.Validate`:

- Quest metadata issues.
- Missing or ambiguous references.
- Location data problems.
- Invalid quantities.
- Generic step warnings.
- Output path issues.
- Module Lua strict validation constraints.

The diagnostics panel shows graph and spec diagnostics together. Clicking a diagnostic selects the relevant graph node or inspector section.

## Generation And Preview

The visual editor never generates Lua or SQL directly from UI controls.

Pipeline:

```text
QuestSpec
  -> QuestGraphProjector
  -> visual edits
  -> QuestGraphValidator
  -> QuestGraphLinearizer
  -> QuestSpecValidator
  -> QuestWorkflow.Preview or GenerateFromSpecAsync
```

Preview refresh uses the existing generation mode on `QuestSpec`. The Lua preview must support both legacy and module Lua output. When blocking validation errors exist, the preview shows the last valid output with a stale-preview warning.

## Testing

Core tests:

- Project sequential `QuestSpec` to graph and back without data loss.
- Project parallel stages and preserve `IsParallel`.
- Project random-option stages and preserve `RandomOptions`.
- Preserve all ten step types.
- Reorder stages and steps, then verify generated numbers and layout repair.
- Serialize and deserialize visual metadata in `.quest.json`.
- Validate unsupported graph shapes.
- Ensure existing generation output remains unchanged for unchanged specs.

Desktop tests or smoke checks where practical:

- Open a spec in the visual editor.
- Select nodes and edit inspector fields.
- Save and reload the same `.quest.json`.
- Preview legacy and module Lua.
- Generate through existing workflow.

Baseline:

- Existing test suite must remain passing.

## Acceptance Criteria

Initial implementation is ready when:

1. Standalone visual editor can create or open a `.quest.json`.
2. `QuestParser.Desktop` opens the same editor in a separate full-size window.
3. `.quest.json` remains the only quest-data file.
4. Visual metadata is stored in a versioned section of `QuestSpec`.
5. Sequential stages render and edit correctly.
6. Parallel stages render as fan-out/fan-in and generate with existing parallel behavior.
7. Random-option kill/kill-by-race stages render and round-trip correctly.
8. All current `StepType` values are available in the palette and inspector.
9. Graph edits linearize back to `QuestSpec` before preview and generation.
10. Lua, SQL, spawn starter, spec JSON, and missing report previews use existing workflow services.
11. Existing parser review/generation behavior remains intact.
12. Existing CLI commands remain supported.
13. Existing tests pass, with new tests covering graph projection, validation, serialization, and round trips.

## Out Of Scope For MVP

- Arbitrary Choice-style conditional branching.
- Loops.
- Failure paths.
- Live game-server simulation.
- Lua reverse engineering.
- Multi-user editing.
- A separate `.eq2questgraph.json` project file.
