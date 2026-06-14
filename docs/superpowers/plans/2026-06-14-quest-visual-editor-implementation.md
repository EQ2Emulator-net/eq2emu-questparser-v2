# Quest Visual Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an AWS-Step-Functions-style visual quest editor that edits the existing `.quest.json` `QuestSpec` model, supports standalone and integrated desktop launch, and preserves current QuestParser generation parity.

**Architecture:** The implementation is spec-first: `QuestSpec` remains canonical, with a versioned visual editor metadata section for layout and review state. Core services project specs into graph sessions, validate current QuestParser graph semantics, and normalize graph edits back into stages/steps before existing preview and generation services run. The Avalonia desktop app hosts a reusable visual editor window used by both standalone launch and the current parser window.

**Tech Stack:** .NET 9, C# nullable reference types, System.Text.Json source generation, Avalonia 12, xUnit, existing `QuestParser.Core` workflow/generator services.

---

## File Structure

Core files:

- Create `src/QuestParser.Core/VisualEditorModels.cs`: persisted visual editor state stored in `.quest.json`.
- Create `src/QuestParser.Core/QuestGraphModels.cs`: non-persisted graph/session DTOs used by the editor.
- Create `src/QuestParser.Core/QuestGraphLayoutService.cs`: deterministic top-down layout and layout repair.
- Create `src/QuestParser.Core/QuestGraphProjector.cs`: `QuestSpec` to graph conversion.
- Create `src/QuestParser.Core/QuestGraphLinearizer.cs`: graph order and editor operations back to `QuestSpec`.
- Create `src/QuestParser.Core/QuestGraphValidator.cs`: graph-shape validation for current QuestParser semantics.
- Modify `src/QuestParser.Core/Models.cs`: add nullable `VisualEditor` property to `QuestSpec`.
- Modify `src/QuestParser.Core/JsonContexts.cs`: add visual editor JSON types to source generation.

Desktop files:

- Create `src/QuestParser.Desktop/VisualEditorWindow.axaml`: visual editor shell.
- Create `src/QuestParser.Desktop/VisualEditorWindow.axaml.cs`: editor window orchestration.
- Create `src/QuestParser.Desktop/VisualEditorViewModel.cs`: editor state, graph projection, validation, preview, dirty state.
- Create `src/QuestParser.Desktop/QuestGraphCanvas.cs`: custom Avalonia graph canvas.
- Create `src/QuestParser.Desktop/VisualEditorDefinitionBuilder.cs`: text definition view builder for selected graph/spec elements.
- Modify `src/QuestParser.Desktop/App.axaml.cs`: open visual editor as the main window when launched with `--visual-editor`.
- Modify `src/QuestParser.Desktop/MainWindow.axaml`: add integrated visual editor launch menu item.
- Modify `src/QuestParser.Desktop/MainWindow.axaml.cs`: open editor from loaded spec and refresh existing review state after save.

Tests and docs:

- Create `tests/QuestParser.Tests/QuestVisualEditorStateTests.cs`: serialization tests.
- Create `tests/QuestParser.Tests/QuestGraphProjectionTests.cs`: projection/layout tests.
- Create `tests/QuestParser.Tests/QuestGraphLinearizerTests.cs`: graph edit normalization tests.
- Create `tests/QuestParser.Tests/QuestGraphValidatorTests.cs`: graph validation tests.
- Modify `README.md`: document visual editor launch and scope.
- Modify `QUEST_PARSER_USER_GUIDE.md`: add visual editor workflow.

---

### Task 1: Persist Visual Editor State In QuestSpec

**Files:**
- Create: `src/QuestParser.Core/VisualEditorModels.cs`
- Modify: `src/QuestParser.Core/Models.cs`
- Modify: `src/QuestParser.Core/JsonContexts.cs`
- Test: `tests/QuestParser.Tests/QuestVisualEditorStateTests.cs`

- [ ] **Step 1: Write the failing serialization tests**

Create `tests/QuestParser.Tests/QuestVisualEditorStateTests.cs`:

```csharp
using System.Text.Json;
using QuestParser.Core;

namespace QuestParser.Tests;

public sealed class QuestVisualEditorStateTests
{
    [Fact]
    public void QuestSpecSerializesVisualEditorState()
    {
        var spec = new QuestSpec
        {
            Quest = new QuestMetadata { Name = "Graph Quest", Zone = "Antonica" },
            VisualEditor = new QuestVisualEditorState
            {
                Viewport = new QuestGraphViewport { X = 12, Y = 34, Zoom = 1.25 },
                Nodes =
                [
                    new QuestGraphNodeLayout
                    {
                        Id = "stage-1-step-1",
                        Kind = QuestGraphNodeKind.Step,
                        StageNumber = 1,
                        StepNumber = 1,
                        X = 320,
                        Y = 180,
                        Width = 260,
                        Height = 72,
                        ReviewStatus = QuestVisualReviewStatus.NeedsReview
                    }
                ]
            }
        };

        var json = JsonSerializer.Serialize(spec, QuestSpecJsonContext.Default.QuestSpec);

        Assert.Contains("\"visualEditor\"", json);
        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.Contains("\"kind\": \"Step\"", json);
        Assert.Contains("\"reviewStatus\": \"NeedsReview\"", json);
    }

    [Fact]
    public void QuestSpecDeserializesVisualEditorState()
    {
        const string json = """
        {
          "schemaVersion": "1.0",
          "generationMode": "LegacySpawnStub",
          "quest": { "name": "Graph Quest", "zone": "Antonica" },
          "output": {},
          "provenance": {},
          "questGivers": [],
          "questId": { "kind": "quest", "query": "", "status": "Missing", "source": "Unresolved" },
          "giver": { "kind": "npc", "query": "", "status": "Missing", "source": "Unresolved" },
          "stages": [],
          "rewards": {},
          "todos": [],
          "generation": {},
          "visualEditor": {
            "schemaVersion": 1,
            "layoutVersion": 1,
            "viewport": { "x": 12, "y": 34, "zoom": 1.25 },
            "nodes": [
              {
                "id": "stage-1-step-1",
                "kind": "Step",
                "stageNumber": 1,
                "stepNumber": 1,
                "x": 320,
                "y": 180,
                "width": 260,
                "height": 72,
                "collapsed": false,
                "reviewStatus": "Reviewed"
              }
            ]
          }
        }
        """;

        var spec = JsonSerializer.Deserialize(json, QuestSpecJsonContext.Default.QuestSpec);

        Assert.NotNull(spec);
        Assert.NotNull(spec.VisualEditor);
        Assert.Equal(12, spec.VisualEditor.Viewport.X);
        Assert.Equal(1.25, spec.VisualEditor.Viewport.Zoom);
        Assert.Single(spec.VisualEditor.Nodes);
        Assert.Equal(QuestGraphNodeKind.Step, spec.VisualEditor.Nodes[0].Kind);
        Assert.Equal(QuestVisualReviewStatus.Reviewed, spec.VisualEditor.Nodes[0].ReviewStatus);
    }
}
```

- [ ] **Step 2: Run the failing test**

Run:

```powershell
dotnet test --no-restore --filter QuestVisualEditorStateTests
```

Expected: FAIL because `QuestVisualEditorState`, `QuestGraphViewport`, `QuestGraphNodeLayout`, `QuestGraphNodeKind`, and `QuestVisualReviewStatus` are not defined.

- [ ] **Step 3: Add persisted visual editor models**

Create `src/QuestParser.Core/VisualEditorModels.cs`:

```csharp
using System.Text.Json.Serialization;

namespace QuestParser.Core;

[JsonConverter(typeof(JsonStringEnumConverter<QuestGraphNodeKind>))]
public enum QuestGraphNodeKind
{
    Start,
    Complete,
    Stage,
    Step,
    RandomOptions,
    RandomOption,
    Comment
}

[JsonConverter(typeof(JsonStringEnumConverter<QuestVisualReviewStatus>))]
public enum QuestVisualReviewStatus
{
    Imported,
    NeedsReview,
    Reviewed,
    Modified,
    Invalid
}

public sealed class QuestVisualEditorState
{
    public int SchemaVersion { get; set; } = 1;
    public int LayoutVersion { get; set; } = 1;
    public QuestGraphViewport Viewport { get; set; } = new();
    public List<QuestGraphNodeLayout> Nodes { get; set; } = [];
}

public sealed class QuestGraphViewport
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Zoom { get; set; } = 1;
}

public sealed class QuestGraphNodeLayout
{
    public string Id { get; set; } = "";
    public QuestGraphNodeKind Kind { get; set; }
    public int? StageNumber { get; set; }
    public int? StepNumber { get; set; }
    public int? OptionIndex { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 260;
    public double Height { get; set; } = 72;
    public bool Collapsed { get; set; }
    public QuestVisualReviewStatus ReviewStatus { get; set; } = QuestVisualReviewStatus.NeedsReview;
}
```

- [ ] **Step 4: Add VisualEditor to QuestSpec**

In `src/QuestParser.Core/Models.cs`, add this property to `QuestSpec` immediately after `Generation`:

```csharp
public QuestVisualEditorState? VisualEditor { get; set; }
```

The `QuestSpec` class should end with these properties:

```csharp
public List<string> Todos { get; set; } = [];
public GenerationStatus Generation { get; set; } = new();
public QuestVisualEditorState? VisualEditor { get; set; }
```

- [ ] **Step 5: Include visual editor types in JSON source generation**

In `src/QuestParser.Core/JsonContexts.cs`, add these attributes above `QuestSpecJsonContext`:

```csharp
[JsonSerializable(typeof(QuestVisualEditorState))]
[JsonSerializable(typeof(QuestGraphNodeLayout))]
[JsonSerializable(typeof(QuestGraphViewport))]
```

The bottom context block should be:

```csharp
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(QuestSpec))]
[JsonSerializable(typeof(QuestVisualEditorState))]
[JsonSerializable(typeof(QuestGraphNodeLayout))]
[JsonSerializable(typeof(QuestGraphViewport))]
public sealed partial class QuestSpecJsonContext : JsonSerializerContext;
```

- [ ] **Step 6: Run the focused tests**

Run:

```powershell
dotnet test --no-restore --filter QuestVisualEditorStateTests
```

Expected: PASS with 2 tests.

- [ ] **Step 7: Commit**

Run:

```powershell
git add src\QuestParser.Core\VisualEditorModels.cs src\QuestParser.Core\Models.cs src\QuestParser.Core\JsonContexts.cs tests\QuestParser.Tests\QuestVisualEditorStateTests.cs
git commit -m "Add quest visual editor state"
```

---

### Task 2: Project QuestSpec Into A Visual Graph

**Files:**
- Create: `src/QuestParser.Core/QuestGraphModels.cs`
- Create: `src/QuestParser.Core/QuestGraphLayoutService.cs`
- Create: `src/QuestParser.Core/QuestGraphProjector.cs`
- Test: `tests/QuestParser.Tests/QuestGraphProjectionTests.cs`

- [ ] **Step 1: Write failing projection tests**

Create `tests/QuestParser.Tests/QuestGraphProjectionTests.cs`:

```csharp
using QuestParser.Core;

namespace QuestParser.Tests;

public sealed class QuestGraphProjectionTests
{
    [Fact]
    public void ProjectorCreatesStartStagesStepsAndComplete()
    {
        var spec = BuildSpec();
        var graph = new QuestGraphProjector().Project(spec);

        Assert.Contains(graph.Nodes, node => node.Kind == QuestGraphNodeKind.Start);
        Assert.Contains(graph.Nodes, node => node.Kind == QuestGraphNodeKind.Complete);
        Assert.Contains(graph.Nodes, node => node.Id == "stage-1");
        Assert.Contains(graph.Nodes, node => node.Id == "stage-1-step-1");
        Assert.Contains(graph.Nodes, node => node.Id == "stage-2");
        Assert.Contains(graph.Nodes, node => node.Id == "stage-2-step-2");
        Assert.Contains(graph.Edges, edge => edge.SourceNodeId == "start" && edge.TargetNodeId == "stage-1");
        Assert.Contains(graph.Edges, edge => edge.SourceNodeId == "stage-1-step-1" && edge.TargetNodeId == "stage-2");
        Assert.Contains(graph.Edges, edge => edge.SourceNodeId == "stage-2-step-2" && edge.TargetNodeId == "complete");
    }

    [Fact]
    public void ProjectorRepresentsParallelStageWithFanOutAndFanIn()
    {
        var spec = BuildSpec();
        spec.Stages[0].IsParallel = true;
        spec.Stages[0].Steps.Add(new QuestStepSpec
        {
            Number = 3,
            Type = StepType.Chat,
            Description = "Speak with the scout",
            CompletedDescription = "Spoke with the scout",
            QuantityMax = 1
        });

        var graph = new QuestGraphProjector().Project(spec);

        var parallelStage = Assert.Single(graph.Nodes.Where(node => node.Id == "stage-1"));
        Assert.True(parallelStage.IsParallelStage);
        Assert.Contains(graph.Edges, edge => edge.SourceNodeId == "stage-1" && edge.TargetNodeId == "stage-1-step-1");
        Assert.Contains(graph.Edges, edge => edge.SourceNodeId == "stage-1" && edge.TargetNodeId == "stage-1-step-3");
        Assert.Contains(graph.Edges, edge => edge.SourceNodeId == "stage-1-step-1" && edge.TargetNodeId == "stage-1-join");
        Assert.Contains(graph.Edges, edge => edge.SourceNodeId == "stage-1-step-3" && edge.TargetNodeId == "stage-1-join");
    }

    [Fact]
    public void ProjectorRepresentsRandomOptionsAsSingleGeneratedStep()
    {
        var spec = BuildSpec();
        spec.Stages[0].Steps[0].Type = StepType.Kill;
        spec.Stages[0].Steps[0].RandomOptions.Add(new QuestStepOptionSpec
        {
            Description = "Kill a gnoll",
            CompletedDescription = "Killed a gnoll",
            QuantityMax = 1,
            SearchText = "gnoll",
            Target = ResolvedReference.Missing("npc", "gnoll")
        });

        var graph = new QuestGraphProjector().Project(spec);
        var node = Assert.Single(graph.Nodes.Where(node => node.Id == "stage-1-step-1"));

        Assert.Equal(QuestGraphNodeKind.RandomOptions, node.Kind);
        Assert.Equal(1, node.RandomOptionCount);
    }

    private static QuestSpec BuildSpec()
    {
        return new QuestSpec
        {
            Quest = new QuestMetadata { Name = "Graph Quest", Zone = "Antonica" },
            Stages =
            [
                new QuestStageSpec
                {
                    Number = 1,
                    Description = "First task group",
                    CompletedDescription = "First task group complete",
                    Steps =
                    [
                        new QuestStepSpec
                        {
                            Number = 1,
                            Type = StepType.Kill,
                            Description = "Kill a wolf",
                            CompletedDescription = "Killed a wolf",
                            QuantityMax = 1
                        }
                    ]
                },
                new QuestStageSpec
                {
                    Number = 2,
                    Description = "Second task group",
                    CompletedDescription = "Second task group complete",
                    Steps =
                    [
                        new QuestStepSpec
                        {
                            Number = 2,
                            Type = StepType.Chat,
                            Description = "Return to the guard",
                            CompletedDescription = "Returned to the guard",
                            QuantityMax = 1
                        }
                    ]
                }
            ]
        };
    }
}
```

- [ ] **Step 2: Run the failing projection tests**

Run:

```powershell
dotnet test --no-restore --filter QuestGraphProjectionTests
```

Expected: FAIL because the graph projection types are not defined.

- [ ] **Step 3: Add graph session models**

Create `src/QuestParser.Core/QuestGraphModels.cs`:

```csharp
namespace QuestParser.Core;

public sealed class QuestGraph
{
    public List<QuestGraphNode> Nodes { get; set; } = [];
    public List<QuestGraphEdge> Edges { get; set; } = [];
}

public sealed class QuestGraphNode
{
    public string Id { get; set; } = "";
    public QuestGraphNodeKind Kind { get; set; }
    public int? StageNumber { get; set; }
    public int? StepNumber { get; set; }
    public int? StageIndex { get; set; }
    public int? StepIndex { get; set; }
    public StepType? StepType { get; set; }
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public bool IsParallelStage { get; set; }
    public int RandomOptionCount { get; set; }
    public QuestGraphNodeLayout Layout { get; set; } = new();
}

public sealed class QuestGraphEdge
{
    public string Id { get; set; } = "";
    public string SourceNodeId { get; set; } = "";
    public string TargetNodeId { get; set; } = "";
    public string Label { get; set; } = "";
}
```

- [ ] **Step 4: Add layout service**

Create `src/QuestParser.Core/QuestGraphLayoutService.cs`:

```csharp
namespace QuestParser.Core;

public sealed class QuestGraphLayoutService
{
    private const double CenterX = 420;
    private const double StageSpacingY = 180;
    private const double StepSpacingX = 300;
    private const double NodeWidth = 260;
    private const double StepHeight = 72;
    private const double StageHeight = 54;
    private const double CircleSize = 48;

    public QuestGraphNodeLayout LayoutFor(QuestSpec spec, QuestGraphNode node, int orderIndex, int siblingIndex = 0, int siblingCount = 1)
    {
        var existing = FindExistingLayout(spec.VisualEditor, node);
        if (existing is not null)
            return existing;

        var y = 60 + orderIndex * StageSpacingY;
        var x = CenterX;
        if (siblingCount > 1)
            x = CenterX + (siblingIndex - (siblingCount - 1) / 2.0) * StepSpacingX;

        var width = node.Kind is QuestGraphNodeKind.Start or QuestGraphNodeKind.Complete ? CircleSize : NodeWidth;
        var height = node.Kind is QuestGraphNodeKind.Stage ? StageHeight : StepHeight;
        if (node.Kind is QuestGraphNodeKind.Start or QuestGraphNodeKind.Complete)
            height = CircleSize;

        return new QuestGraphNodeLayout
        {
            Id = node.Id,
            Kind = node.Kind,
            StageNumber = node.StageNumber,
            StepNumber = node.StepNumber,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            ReviewStatus = QuestVisualReviewStatus.NeedsReview
        };
    }

    public void EnsureVisualState(QuestSpec spec, QuestGraph graph)
    {
        spec.VisualEditor ??= new QuestVisualEditorState();
        var retained = new List<QuestGraphNodeLayout>();
        foreach (var node in graph.Nodes)
            retained.Add(node.Layout);
        spec.VisualEditor.Nodes = retained;
    }

    private static QuestGraphNodeLayout? FindExistingLayout(QuestVisualEditorState? state, QuestGraphNode node)
    {
        if (state is null)
            return null;

        var exact = state.Nodes.FirstOrDefault(layout => string.Equals(layout.Id, node.Id, StringComparison.Ordinal));
        if (exact is not null)
            return exact;

        return state.Nodes.FirstOrDefault(layout =>
            layout.Kind == node.Kind
            && layout.StageNumber == node.StageNumber
            && layout.StepNumber == node.StepNumber);
    }
}
```

- [ ] **Step 5: Add graph projector**

Create `src/QuestParser.Core/QuestGraphProjector.cs`:

```csharp
namespace QuestParser.Core;

public sealed class QuestGraphProjector
{
    private readonly QuestGraphLayoutService _layoutService;

    public QuestGraphProjector(QuestGraphLayoutService? layoutService = null)
    {
        _layoutService = layoutService ?? new QuestGraphLayoutService();
    }

    public QuestGraph Project(QuestSpec spec)
    {
        var graph = new QuestGraph();
        var order = 0;

        var start = CreateNode("start", QuestGraphNodeKind.Start, "Start", spec.Quest.Name);
        start.Layout = _layoutService.LayoutFor(spec, start, order++);
        graph.Nodes.Add(start);

        string previousExit = start.Id;
        foreach (var stage in spec.Stages.OrderBy(stage => stage.Number))
        {
            var stageIndex = spec.Stages.IndexOf(stage);
            var stageNode = CreateNode(
                $"stage-{stage.Number}",
                QuestGraphNodeKind.Stage,
                $"Stage {stage.Number}",
                stage.Description);
            stageNode.StageNumber = stage.Number;
            stageNode.StageIndex = stageIndex;
            stageNode.IsParallelStage = stage.IsParallel;
            stageNode.Layout = _layoutService.LayoutFor(spec, stageNode, order++);
            graph.Nodes.Add(stageNode);
            graph.Edges.Add(CreateEdge(previousExit, stageNode.Id, ""));

            if (stage.IsParallel && stage.Steps.Count > 1)
            {
                var joinNode = CreateNode(
                    $"stage-{stage.Number}-join",
                    QuestGraphNodeKind.Stage,
                    $"Stage {stage.Number} complete",
                    stage.CompletedDescription);
                joinNode.StageNumber = stage.Number;
                joinNode.StageIndex = stageIndex;
                joinNode.Layout = _layoutService.LayoutFor(spec, joinNode, order);
                graph.Nodes.Add(joinNode);

                for (var i = 0; i < stage.Steps.Count; i++)
                {
                    var stepNode = CreateStepNode(spec, stage, stageIndex, stage.Steps[i], i);
                    stepNode.Layout = _layoutService.LayoutFor(spec, stepNode, order, i, stage.Steps.Count);
                    graph.Nodes.Add(stepNode);
                    graph.Edges.Add(CreateEdge(stageNode.Id, stepNode.Id, "parallel"));
                    graph.Edges.Add(CreateEdge(stepNode.Id, joinNode.Id, "complete"));
                }

                previousExit = joinNode.Id;
                order++;
            }
            else
            {
                string prior = stageNode.Id;
                for (var i = 0; i < stage.Steps.Count; i++)
                {
                    var stepNode = CreateStepNode(spec, stage, stageIndex, stage.Steps[i], i);
                    stepNode.Layout = _layoutService.LayoutFor(spec, stepNode, order++);
                    graph.Nodes.Add(stepNode);
                    graph.Edges.Add(CreateEdge(prior, stepNode.Id, ""));
                    prior = stepNode.Id;
                }

                previousExit = prior;
            }
        }

        var complete = CreateNode("complete", QuestGraphNodeKind.Complete, "Complete", spec.Quest.CompletionText);
        complete.Layout = _layoutService.LayoutFor(spec, complete, order);
        graph.Nodes.Add(complete);
        graph.Edges.Add(CreateEdge(previousExit, complete.Id, ""));

        _layoutService.EnsureVisualState(spec, graph);
        return graph;
    }

    private static QuestGraphNode CreateStepNode(QuestSpec spec, QuestStageSpec stage, int stageIndex, QuestStepSpec step, int stepIndex)
    {
        var kind = step.HasRandomOptions ? QuestGraphNodeKind.RandomOptions : QuestGraphNodeKind.Step;
        return new QuestGraphNode
        {
            Id = $"stage-{stage.Number}-step-{step.Number}",
            Kind = kind,
            StageNumber = stage.Number,
            StepNumber = step.Number,
            StageIndex = stageIndex,
            StepIndex = stepIndex,
            StepType = step.Type,
            Title = $"{step.Type} Step {step.Number}",
            Subtitle = step.Description,
            RandomOptionCount = step.RandomOptions.Count
        };
    }

    private static QuestGraphNode CreateNode(string id, QuestGraphNodeKind kind, string title, string subtitle)
    {
        return new QuestGraphNode
        {
            Id = id,
            Kind = kind,
            Title = title,
            Subtitle = subtitle
        };
    }

    private static QuestGraphEdge CreateEdge(string sourceNodeId, string targetNodeId, string label)
    {
        return new QuestGraphEdge
        {
            Id = $"{sourceNodeId}->{targetNodeId}",
            SourceNodeId = sourceNodeId,
            TargetNodeId = targetNodeId,
            Label = label
        };
    }
}
```

- [ ] **Step 6: Run projection tests**

Run:

```powershell
dotnet test --no-restore --filter QuestGraphProjectionTests
```

Expected: PASS.

- [ ] **Step 7: Commit**

Run:

```powershell
git add src\QuestParser.Core\QuestGraphModels.cs src\QuestParser.Core\QuestGraphLayoutService.cs src\QuestParser.Core\QuestGraphProjector.cs tests\QuestParser.Tests\QuestGraphProjectionTests.cs
git commit -m "Project quest specs into visual graphs"
```

---

### Task 3: Add Graph Linearization Operations

**Files:**
- Create: `src/QuestParser.Core/QuestGraphLinearizer.cs`
- Test: `tests/QuestParser.Tests/QuestGraphLinearizerTests.cs`

- [ ] **Step 1: Write failing linearizer tests**

Create `tests/QuestParser.Tests/QuestGraphLinearizerTests.cs`:

```csharp
using QuestParser.Core;

namespace QuestParser.Tests;

public sealed class QuestGraphLinearizerTests
{
    [Fact]
    public void MoveStageRenumbersStagesAndSteps()
    {
        var spec = BuildSpec();
        new QuestGraphLinearizer().MoveStage(spec, fromStageIndex: 1, toStageIndex: 0);

        Assert.Equal("Second", spec.Stages[0].Description);
        Assert.Equal(1, spec.Stages[0].Number);
        Assert.Equal(1, spec.Stages[0].Steps[0].Number);
        Assert.Equal("First", spec.Stages[1].Description);
        Assert.Equal(2, spec.Stages[1].Number);
        Assert.Equal(2, spec.Stages[1].Steps[0].Number);
    }

    [Fact]
    public void MoveStepBetweenStagesRenumbersSteps()
    {
        var spec = BuildSpec();
        new QuestGraphLinearizer().MoveStep(spec, fromStageIndex: 0, fromStepIndex: 0, toStageIndex: 1, toStepIndex: 1);

        Assert.Empty(spec.Stages[0].Steps);
        Assert.Equal(2, spec.Stages[1].Steps.Count);
        Assert.Equal(1, spec.Stages[1].Steps[0].Number);
        Assert.Equal("Chat second", spec.Stages[1].Steps[0].Description);
        Assert.Equal(2, spec.Stages[1].Steps[1].Number);
        Assert.Equal("Kill first", spec.Stages[1].Steps[1].Description);
    }

    [Fact]
    public void AddStepCreatesValidStepWithMatchingReferenceKind()
    {
        var spec = BuildSpec();
        var step = new QuestGraphLinearizer().AddStep(spec, stageIndex: 0, StepType.ZoneLocation);

        Assert.Equal(StepType.ZoneLocation, step.Type);
        Assert.Equal("location", step.Target.Kind);
        Assert.NotNull(step.Location);
        Assert.Equal("zone", step.Location.Zone.Kind);
        Assert.Equal(1, step.QuantityMax);
    }

    private static QuestSpec BuildSpec()
    {
        return new QuestSpec
        {
            Quest = new QuestMetadata { Name = "Graph Quest", Zone = "Antonica" },
            Stages =
            [
                new QuestStageSpec
                {
                    Number = 1,
                    Description = "First",
                    Steps =
                    [
                        new QuestStepSpec
                        {
                            Number = 1,
                            Type = StepType.Kill,
                            Description = "Kill first",
                            CompletedDescription = "Killed first",
                            QuantityMax = 1
                        }
                    ]
                },
                new QuestStageSpec
                {
                    Number = 2,
                    Description = "Second",
                    Steps =
                    [
                        new QuestStepSpec
                        {
                            Number = 2,
                            Type = StepType.Chat,
                            Description = "Chat second",
                            CompletedDescription = "Chatted second",
                            QuantityMax = 1
                        }
                    ]
                }
            ]
        };
    }
}
```

- [ ] **Step 2: Run the failing linearizer tests**

Run:

```powershell
dotnet test --no-restore --filter QuestGraphLinearizerTests
```

Expected: FAIL because `QuestGraphLinearizer` is not defined.

- [ ] **Step 3: Add linearizer**

Create `src/QuestParser.Core/QuestGraphLinearizer.cs`:

```csharp
namespace QuestParser.Core;

public sealed class QuestGraphLinearizer
{
    public void MoveStage(QuestSpec spec, int fromStageIndex, int toStageIndex)
    {
        if (fromStageIndex < 0 || fromStageIndex >= spec.Stages.Count)
            throw new ArgumentOutOfRangeException(nameof(fromStageIndex));
        if (toStageIndex < 0 || toStageIndex >= spec.Stages.Count)
            throw new ArgumentOutOfRangeException(nameof(toStageIndex));

        var stage = spec.Stages[fromStageIndex];
        spec.Stages.RemoveAt(fromStageIndex);
        spec.Stages.Insert(toStageIndex, stage);
        NormalizeNumbers(spec);
    }

    public void MoveStep(QuestSpec spec, int fromStageIndex, int fromStepIndex, int toStageIndex, int toStepIndex)
    {
        var fromStage = spec.Stages[fromStageIndex];
        var toStage = spec.Stages[toStageIndex];
        var step = fromStage.Steps[fromStepIndex];
        fromStage.Steps.RemoveAt(fromStepIndex);

        var boundedIndex = Math.Clamp(toStepIndex, 0, toStage.Steps.Count);
        toStage.Steps.Insert(boundedIndex, step);
        NormalizeNumbers(spec);
    }

    public QuestStageSpec AddStage(QuestSpec spec, bool isParallel)
    {
        var stage = new QuestStageSpec
        {
            Description = isParallel ? "Parallel task group" : "Task group",
            CompletedDescription = isParallel ? "Parallel task group complete" : "Task group complete",
            IsParallel = isParallel
        };
        spec.Stages.Add(stage);
        NormalizeNumbers(spec);
        return stage;
    }

    public QuestStepSpec AddStep(QuestSpec spec, int stageIndex, StepType stepType)
    {
        var stage = spec.Stages[stageIndex];
        var kind = QuestSpecFactory.KindForStepType(stepType);
        var step = new QuestStepSpec
        {
            Type = stepType,
            Description = $"{DisplayName(stepType)} objective",
            CompletedDescription = $"{DisplayName(stepType)} objective complete",
            QuantityMin = 0,
            QuantityMax = 1,
            Percentage = 100,
            SearchText = "",
            Target = ResolvedReference.Missing(kind, "")
        };

        if (stepType is StepType.Location or StepType.ZoneLocation)
        {
            step.Location = new LocationTarget
            {
                Radius = 10,
                Zone = ResolvedReference.Missing("zone", spec.Quest.Zone)
            };
        }

        stage.Steps.Add(step);
        NormalizeNumbers(spec);
        return step;
    }

    public void RemoveStep(QuestSpec spec, int stageIndex, int stepIndex)
    {
        spec.Stages[stageIndex].Steps.RemoveAt(stepIndex);
        NormalizeNumbers(spec);
    }

    public void SetStageParallel(QuestSpec spec, int stageIndex, bool isParallel)
    {
        spec.Stages[stageIndex].IsParallel = isParallel;
        NormalizeNumbers(spec);
    }

    public void NormalizeNumbers(QuestSpec spec)
    {
        var nextStepNumber = 1;
        for (var stageIndex = 0; stageIndex < spec.Stages.Count; stageIndex++)
        {
            var stage = spec.Stages[stageIndex];
            stage.Number = stageIndex + 1;
            foreach (var step in stage.Steps)
                step.Number = nextStepNumber++;
        }
    }

    private static string DisplayName(StepType type)
    {
        return type switch
        {
            StepType.KillByRace => "Kill by race",
            StepType.ObtainItem => "Obtain item",
            StepType.ZoneLocation => "Zone location",
            _ => type.ToString()
        };
    }
}
```

- [ ] **Step 4: Run linearizer tests**

Run:

```powershell
dotnet test --no-restore --filter QuestGraphLinearizerTests
```

Expected: PASS.

- [ ] **Step 5: Run projection tests to catch numbering regressions**

Run:

```powershell
dotnet test --no-restore --filter "QuestGraphProjectionTests|QuestGraphLinearizerTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

Run:

```powershell
git add src\QuestParser.Core\QuestGraphLinearizer.cs tests\QuestParser.Tests\QuestGraphLinearizerTests.cs
git commit -m "Add quest graph linearizer operations"
```

---

### Task 4: Validate Graph Shape Against Current Parser Semantics

**Files:**
- Create: `src/QuestParser.Core/QuestGraphValidator.cs`
- Test: `tests/QuestParser.Tests/QuestGraphValidatorTests.cs`

- [ ] **Step 1: Write failing validator tests**

Create `tests/QuestParser.Tests/QuestGraphValidatorTests.cs`:

```csharp
using QuestParser.Core;

namespace QuestParser.Tests;

public sealed class QuestGraphValidatorTests
{
    [Fact]
    public void ValidSequentialGraphHasNoBlockers()
    {
        var spec = BuildSpec(isParallel: false);
        var graph = new QuestGraphProjector().Project(spec);
        var diagnostics = new QuestGraphValidator().Validate(graph);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == QuestDiagnosticSeverity.Blocker);
    }

    [Fact]
    public void MissingCompleteNodeIsBlocker()
    {
        var spec = BuildSpec(isParallel: false);
        var graph = new QuestGraphProjector().Project(spec);
        graph.Nodes.RemoveAll(node => node.Kind == QuestGraphNodeKind.Complete);

        var diagnostics = new QuestGraphValidator().Validate(graph);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "GRAPH_COMPLETE_COUNT");
    }

    [Fact]
    public void ParallelStageWithoutJoinIsBlocker()
    {
        var spec = BuildSpec(isParallel: true);
        spec.Stages[0].Steps.Add(new QuestStepSpec
        {
            Number = 2,
            Type = StepType.Chat,
            Description = "Speak",
            CompletedDescription = "Spoke",
            QuantityMax = 1
        });
        var graph = new QuestGraphProjector().Project(spec);
        graph.Nodes.RemoveAll(node => node.Id == "stage-1-join");

        var diagnostics = new QuestGraphValidator().Validate(graph);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "GRAPH_PARALLEL_JOIN");
    }

    [Fact]
    public void UnsupportedBranchingIsBlocker()
    {
        var spec = BuildSpec(isParallel: false);
        var graph = new QuestGraphProjector().Project(spec);
        graph.Edges.Add(new QuestGraphEdge
        {
            Id = "stage-1-step-1->complete-extra",
            SourceNodeId = "stage-1-step-1",
            TargetNodeId = "complete",
            Label = "extra"
        });

        var diagnostics = new QuestGraphValidator().Validate(graph);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "GRAPH_UNSUPPORTED_BRANCH");
    }

    private static QuestSpec BuildSpec(bool isParallel)
    {
        return new QuestSpec
        {
            Quest = new QuestMetadata { Name = "Graph Quest", Zone = "Antonica" },
            Stages =
            [
                new QuestStageSpec
                {
                    Number = 1,
                    Description = "First",
                    IsParallel = isParallel,
                    Steps =
                    [
                        new QuestStepSpec
                        {
                            Number = 1,
                            Type = StepType.Kill,
                            Description = "Kill",
                            CompletedDescription = "Killed",
                            QuantityMax = 1
                        }
                    ]
                }
            ]
        };
    }
}
```

- [ ] **Step 2: Run the failing validator tests**

Run:

```powershell
dotnet test --no-restore --filter QuestGraphValidatorTests
```

Expected: FAIL because `QuestGraphValidator` is not defined.

- [ ] **Step 3: Add graph validator**

Create `src/QuestParser.Core/QuestGraphValidator.cs`:

```csharp
namespace QuestParser.Core;

public sealed class QuestGraphValidator
{
    public List<QuestDiagnostic> Validate(QuestGraph graph)
    {
        var diagnostics = new List<QuestDiagnostic>();
        ValidateNodeCounts(graph, diagnostics);
        ValidateEdges(graph, diagnostics);
        ValidateParallelJoins(graph, diagnostics);
        return diagnostics
            .OrderByDescending(diagnostic => diagnostic.Severity)
            .ThenBy(diagnostic => diagnostic.SectionKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(diagnostic => diagnostic.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ValidateNodeCounts(QuestGraph graph, List<QuestDiagnostic> diagnostics)
    {
        var starts = graph.Nodes.Count(node => node.Kind == QuestGraphNodeKind.Start);
        var completes = graph.Nodes.Count(node => node.Kind == QuestGraphNodeKind.Complete);
        if (starts != 1)
            Add(diagnostics, "graph", "GRAPH_START_COUNT", $"Graph must have exactly one Start node; found {starts}.");
        if (completes != 1)
            Add(diagnostics, "graph", "GRAPH_COMPLETE_COUNT", $"Graph must have exactly one Complete node; found {completes}.");
    }

    private static void ValidateEdges(QuestGraph graph, List<QuestDiagnostic> diagnostics)
    {
        var nodeIds = graph.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var edge in graph.Edges)
        {
            if (!nodeIds.Contains(edge.SourceNodeId) || !nodeIds.Contains(edge.TargetNodeId))
                Add(diagnostics, "graph", "GRAPH_DISCONNECTED_EDGE", $"Edge '{edge.Id}' references a missing node.");
        }

        var outgoing = graph.Edges
            .GroupBy(edge => edge.SourceNodeId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        foreach (var node in graph.Nodes)
        {
            if (node.Kind == QuestGraphNodeKind.Stage && node.IsParallelStage)
                continue;
            if (node.Id.EndsWith("-join", StringComparison.Ordinal))
                continue;

            if (outgoing.TryGetValue(node.Id, out var count) && count > 1)
                Add(diagnostics, node.Id, "GRAPH_UNSUPPORTED_BRANCH", $"Node '{node.Title}' has {count} outgoing edges; current QuestParser generation supports only parser-defined parallel fan-out.");
        }
    }

    private static void ValidateParallelJoins(QuestGraph graph, List<QuestDiagnostic> diagnostics)
    {
        foreach (var stage in graph.Nodes.Where(node => node.Kind == QuestGraphNodeKind.Stage && node.IsParallelStage))
        {
            var joinId = $"{stage.Id}-join";
            if (graph.Nodes.All(node => node.Id != joinId))
                Add(diagnostics, stage.Id, "GRAPH_PARALLEL_JOIN", $"Parallel stage '{stage.Title}' must have a generated join node.");
        }
    }

    private static void Add(List<QuestDiagnostic> diagnostics, string sectionKey, string code, string message)
    {
        diagnostics.Add(new QuestDiagnostic
        {
            Severity = QuestDiagnosticSeverity.Blocker,
            SectionKey = sectionKey,
            Code = code,
            Message = message
        });
    }
}
```

- [ ] **Step 4: Run validator tests**

Run:

```powershell
dotnet test --no-restore --filter QuestGraphValidatorTests
```

Expected: PASS.

- [ ] **Step 5: Run all core graph tests**

Run:

```powershell
dotnet test --no-restore --filter "QuestVisualEditorStateTests|QuestGraphProjectionTests|QuestGraphLinearizerTests|QuestGraphValidatorTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

Run:

```powershell
git add src\QuestParser.Core\QuestGraphValidator.cs tests\QuestParser.Tests\QuestGraphValidatorTests.cs
git commit -m "Validate quest graph structure"
```

---

### Task 5: Add Visual Editor ViewModel

**Files:**
- Create: `src/QuestParser.Desktop/VisualEditorViewModel.cs`
- Create: `src/QuestParser.Desktop/VisualEditorDefinitionBuilder.cs`

- [ ] **Step 1: Add definition builder**

Create `src/QuestParser.Desktop/VisualEditorDefinitionBuilder.cs`:

```csharp
using System.Text;
using System.Text.Json;
using QuestParser.Core;

namespace QuestParser.Desktop;

internal static class VisualEditorDefinitionBuilder
{
    public static string Build(QuestSpec? spec, QuestGraphNode? selectedNode)
    {
        if (spec is null)
            return "";

        if (selectedNode is null)
            return JsonSerializer.Serialize(spec, QuestSpecJsonContext.Default.QuestSpec);

        if (selectedNode.StageIndex.HasValue && selectedNode.StepIndex.HasValue)
        {
            var step = spec.Stages[selectedNode.StageIndex.Value].Steps[selectedNode.StepIndex.Value];
            var writer = new StringBuilder();
            writer.AppendLine($"Node: {selectedNode.Id}");
            writer.AppendLine($"Kind: {selectedNode.Kind}");
            writer.AppendLine($"Step: {step.Number}");
            writer.AppendLine($"Type: {step.Type}");
            writer.AppendLine($"Description: {step.Description}");
            writer.AppendLine($"Completed: {step.CompletedDescription}");
            writer.AppendLine($"Quantity: {step.QuantityMin}..{step.QuantityMax}");
            writer.AppendLine($"Search: {step.SearchText}");
            writer.AppendLine($"Target: {step.Target.Kind} {step.Target.Status} {step.Target.Id}");
            return writer.ToString();
        }

        if (selectedNode.StageIndex.HasValue)
        {
            var stage = spec.Stages[selectedNode.StageIndex.Value];
            return $"Node: {selectedNode.Id}{Environment.NewLine}Kind: {selectedNode.Kind}{Environment.NewLine}Stage: {stage.Number}{Environment.NewLine}Parallel: {stage.IsParallel}{Environment.NewLine}Description: {stage.Description}{Environment.NewLine}Completed: {stage.CompletedDescription}";
        }

        return $"Node: {selectedNode.Id}{Environment.NewLine}Kind: {selectedNode.Kind}{Environment.NewLine}Title: {selectedNode.Title}{Environment.NewLine}Subtitle: {selectedNode.Subtitle}";
    }
}
```

- [ ] **Step 2: Add ViewModel**

Create `src/QuestParser.Desktop/VisualEditorViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using QuestParser.Core;

namespace QuestParser.Desktop;

internal sealed class VisualEditorViewModel
{
    private readonly QuestWorkflow _workflow;
    private readonly QuestGraphProjector _projector = new();
    private readonly QuestGraphLinearizer _linearizer = new();
    private readonly QuestGraphValidator _graphValidator = new();

    public VisualEditorViewModel(QuestWorkflow workflow, QuestSpec? spec = null)
    {
        _workflow = workflow;
        Spec = spec;
        RebuildGraph();
    }

    public QuestSpec? Spec { get; private set; }
    public QuestGraph Graph { get; private set; } = new();
    public QuestGraphNode? SelectedNode { get; private set; }
    public ObservableCollection<string> Diagnostics { get; } = [];
    public ObservableCollection<string> GenerationLog { get; } = [];
    public string LuaPreview { get; private set; } = "";
    public string SqlPreview { get; private set; } = "";
    public string MissingPreview { get; private set; } = "";
    public string Walkthrough { get; private set; } = "";
    public string Definition { get; private set; } = "";
    public bool IsDirty { get; private set; }

    public void LoadSpec(QuestSpec spec)
    {
        Spec = spec;
        RebuildGraph();
        RefreshPreview();
        IsDirty = false;
    }

    public void SelectNode(string nodeId)
    {
        SelectedNode = Graph.Nodes.FirstOrDefault(node => string.Equals(node.Id, nodeId, StringComparison.Ordinal));
        Definition = VisualEditorDefinitionBuilder.Build(Spec, SelectedNode);
    }

    public void AddStage(bool isParallel)
    {
        if (Spec is null)
            return;

        _linearizer.AddStage(Spec, isParallel);
        RebuildAfterEdit();
    }

    public void AddStep(int stageIndex, StepType stepType)
    {
        if (Spec is null)
            return;

        _linearizer.AddStep(Spec, stageIndex, stepType);
        RebuildAfterEdit();
    }

    public void MoveNode(string nodeId, double x, double y)
    {
        if (Spec?.VisualEditor is null)
            return;

        var layout = Spec.VisualEditor.Nodes.FirstOrDefault(node => string.Equals(node.Id, nodeId, StringComparison.Ordinal));
        if (layout is null)
            return;

        layout.X = x;
        layout.Y = y;
        IsDirty = true;
        RebuildGraph();
    }

    public IReadOnlyList<QuestDiagnostic> Validate()
    {
        Diagnostics.Clear();
        if (Spec is null)
            return [];

        var graphDiagnostics = _graphValidator.Validate(Graph);
        var specDiagnostics = QuestSpecValidator.Validate(Spec, overwrite: true);
        var diagnostics = graphDiagnostics.Concat(specDiagnostics).ToList();
        if (diagnostics.Count == 0)
        {
            Diagnostics.Add("No diagnostics.");
            return diagnostics;
        }

        foreach (var diagnostic in diagnostics)
            Diagnostics.Add($"{diagnostic.Severity,-7} {diagnostic.SectionKey,-18} {diagnostic.Code,-24} {diagnostic.Message}");
        return diagnostics;
    }

    public void RefreshPreview()
    {
        if (Spec is null)
            return;

        var blockers = Validate().Where(diagnostic => diagnostic.Severity == QuestDiagnosticSeverity.Blocker).ToArray();
        if (blockers.Length > 0 && !string.IsNullOrWhiteSpace(LuaPreview))
        {
            GenerationLog.Add("Preview is stale because validation has blockers.");
            return;
        }

        var result = _workflow.Preview(Spec);
        LuaPreview = result.Lua;
        SqlPreview = result.Sql;
        MissingPreview = result.MissingReport;
        Walkthrough = BuildWalkthrough(Spec);
        Definition = VisualEditorDefinitionBuilder.Build(Spec, SelectedNode);
    }

    public async Task SaveSpecAsync(CancellationToken cancellationToken = default)
    {
        if (Spec is null)
            return;

        await QuestWorkflow.WriteSpecAsync(Spec, cancellationToken).ConfigureAwait(false);
        IsDirty = false;
    }

    public async Task<QuestWorkflowResult?> GenerateAsync(bool overwrite, CancellationToken cancellationToken = default)
    {
        if (Spec is null)
            return null;

        var result = await _workflow.GenerateFromSpecAsync(
            Spec,
            overwrite,
            cancellationToken,
            Spec.GenerationMode,
            strictModuleLuaValidation: true).ConfigureAwait(false);
        IsDirty = false;
        return result;
    }

    private void RebuildAfterEdit()
    {
        IsDirty = true;
        RebuildGraph();
        RefreshPreview();
    }

    private void RebuildGraph()
    {
        Graph = Spec is null ? new QuestGraph() : _projector.Project(Spec);
        if (SelectedNode is not null && Graph.Nodes.All(node => node.Id != SelectedNode.Id))
            SelectedNode = null;
        Definition = VisualEditorDefinitionBuilder.Build(Spec, SelectedNode);
    }

    private static string BuildWalkthrough(QuestSpec spec)
    {
        var lines = new List<string> { $"Start quest: {spec.Quest.Name}" };
        foreach (var stage in spec.Stages.OrderBy(stage => stage.Number))
        {
            lines.Add($"Stage {stage.Number}: {stage.Description}");
            foreach (var step in stage.Steps.OrderBy(step => step.Number))
                lines.Add($"  {step.Number}. [{step.Type}] {step.Description}");
        }
        lines.Add("Complete quest");
        return string.Join(Environment.NewLine, lines);
    }
}
```

- [ ] **Step 3: Build desktop project**

Run:

```powershell
dotnet build src\QuestParser.Desktop\QuestParser.Desktop.csproj --no-restore
```

Expected: PASS.

- [ ] **Step 4: Commit**

Run:

```powershell
git add src\QuestParser.Desktop\VisualEditorViewModel.cs src\QuestParser.Desktop\VisualEditorDefinitionBuilder.cs
git commit -m "Add visual editor view model"
```

---

### Task 6: Add Graph Canvas Rendering And Basic Interaction

**Files:**
- Create: `src/QuestParser.Desktop/QuestGraphCanvas.cs`

- [ ] **Step 1: Add graph canvas control**

Create `src/QuestParser.Desktop/QuestGraphCanvas.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using QuestParser.Core;

namespace QuestParser.Desktop;

internal sealed class QuestGraphCanvas : Control
{
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromRgb(226, 232, 240)), 1);
    private static readonly Pen EdgePen = new(new SolidColorBrush(Color.FromRgb(100, 116, 139)), 1.5);
    private static readonly Pen SelectedPen = new(new SolidColorBrush(Color.FromRgb(15, 98, 254)), 2);
    private static readonly IBrush NodeBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255));
    private static readonly IBrush StageBrush = new SolidColorBrush(Color.FromRgb(234, 242, 255));
    private static readonly IBrush StartEndBrush = new SolidColorBrush(Color.FromRgb(254, 243, 199));
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.FromRgb(15, 23, 42));
    private static readonly Typeface Typeface = new("Inter");

    private QuestGraph _graph = new();
    private string _selectedNodeId = "";
    private QuestGraphNode? _dragNode;
    private Point _dragOffset;

    public event Action<string>? NodeSelected;
    public event Action<string, double, double>? NodeMoved;

    public QuestGraph Graph
    {
        get => _graph;
        set
        {
            _graph = value;
            InvalidateVisual();
        }
    }

    public string SelectedNodeId
    {
        get => _selectedNodeId;
        set
        {
            _selectedNodeId = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        DrawGrid(context);
        DrawEdges(context);
        DrawNodes(context);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetPosition(this);
        _dragNode = HitTestNode(point);
        if (_dragNode is null)
            return;

        SelectedNodeId = _dragNode.Id;
        NodeSelected?.Invoke(_dragNode.Id);
        _dragOffset = new Point(point.X - _dragNode.Layout.X, point.Y - _dragNode.Layout.Y);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragNode is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var point = e.GetPosition(this);
        var x = point.X - _dragOffset.X;
        var y = point.Y - _dragOffset.Y;
        _dragNode.Layout.X = x;
        _dragNode.Layout.Y = y;
        NodeMoved?.Invoke(_dragNode.Id, x, y);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragNode = null;
        e.Pointer.Capture(null);
    }

    private void DrawGrid(DrawingContext context)
    {
        const double spacing = 12;
        for (var x = 0.0; x < Bounds.Width; x += spacing)
            context.DrawLine(GridPen, new Point(x, 0), new Point(x, Bounds.Height));
        for (var y = 0.0; y < Bounds.Height; y += spacing)
            context.DrawLine(GridPen, new Point(0, y), new Point(Bounds.Width, y));
    }

    private void DrawEdges(DrawingContext context)
    {
        foreach (var edge in Graph.Edges)
        {
            var source = Graph.Nodes.FirstOrDefault(node => node.Id == edge.SourceNodeId);
            var target = Graph.Nodes.FirstOrDefault(node => node.Id == edge.TargetNodeId);
            if (source is null || target is null)
                continue;

            var start = new Point(source.Layout.X + source.Layout.Width / 2, source.Layout.Y + source.Layout.Height);
            var end = new Point(target.Layout.X + target.Layout.Width / 2, target.Layout.Y);
            context.DrawLine(EdgePen, start, end);
            if (!string.IsNullOrWhiteSpace(edge.Label))
                DrawText(context, edge.Label, (start.X + end.X) / 2 + 6, (start.Y + end.Y) / 2 - 16, 11, TextBrush);
        }
    }

    private void DrawNodes(DrawingContext context)
    {
        foreach (var node in Graph.Nodes)
        {
            var rect = new Rect(node.Layout.X, node.Layout.Y, node.Layout.Width, node.Layout.Height);
            var brush = node.Kind switch
            {
                QuestGraphNodeKind.Start or QuestGraphNodeKind.Complete => StartEndBrush,
                QuestGraphNodeKind.Stage => StageBrush,
                _ => NodeBrush
            };
            var pen = node.Id == SelectedNodeId ? SelectedPen : EdgePen;
            context.DrawRectangle(brush, pen, rect, 4);
            DrawText(context, node.Title, rect.X + 12, rect.Y + 8, 13, TextBrush);
            DrawText(context, Trim(node.Subtitle, 38), rect.X + 12, rect.Y + 32, 12, TextBrush);
        }
    }

    private QuestGraphNode? HitTestNode(Point point)
    {
        return Graph.Nodes.LastOrDefault(node =>
            point.X >= node.Layout.X
            && point.X <= node.Layout.X + node.Layout.Width
            && point.Y >= node.Layout.Y
            && point.Y <= node.Layout.Y + node.Layout.Height);
    }

    private static void DrawText(DrawingContext context, string text, double x, double y, double size, IBrush brush)
    {
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface,
            size,
            brush);
        context.DrawText(formatted, new Point(x, y));
    }

    private static string Trim(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;
        return value[..Math.Max(0, maxLength - 3)] + "...";
    }
}
```

- [ ] **Step 2: Build desktop project**

Run:

```powershell
dotnet build src\QuestParser.Desktop\QuestParser.Desktop.csproj --no-restore
```

Expected: PASS. If Avalonia `FormattedText` constructor overload differs, adjust the constructor call to the Avalonia 12 overload exposed by the installed package and rerun until the project builds.

- [ ] **Step 3: Commit**

Run:

```powershell
git add src\QuestParser.Desktop\QuestGraphCanvas.cs
git commit -m "Render quest graph canvas"
```

---

### Task 7: Add Visual Editor Window Shell

**Files:**
- Create: `src/QuestParser.Desktop/VisualEditorWindow.axaml`
- Create: `src/QuestParser.Desktop/VisualEditorWindow.axaml.cs`

- [ ] **Step 1: Add editor XAML shell**

Create `src/QuestParser.Desktop/VisualEditorWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:QuestParser.Desktop"
        x:Class="QuestParser.Desktop.VisualEditorWindow"
        Title="EQ2Emu Quest Visual Editor"
        Width="1440"
        Height="900"
        MinWidth="1100"
        MinHeight="720">
    <Grid RowDefinitions="Auto,*" Background="#F8FAFC">
        <Grid Grid.Row="0" ColumnDefinitions="Auto,Auto,Auto,Auto,Auto,Auto,Auto,*,Auto,Auto" Height="44" Background="#FFFFFF" ColumnSpacing="8" Margin="0,0,0,1">
            <Button Grid.Column="0" Name="UndoButton" Content="Undo" MinWidth="82" Margin="8,6,0,6" />
            <Button Grid.Column="1" Name="RedoButton" Content="Redo" MinWidth="82" Margin="0,6" />
            <Button Grid.Column="2" Name="ZoomInButton" Content="Zoom in" MinWidth="92" Margin="0,6" />
            <Button Grid.Column="3" Name="ZoomOutButton" Content="Zoom out" MinWidth="96" Margin="0,6" />
            <Button Grid.Column="4" Name="CenterButton" Content="Center" MinWidth="82" Margin="0,6" />
            <Button Grid.Column="5" Name="ValidateButton" Content="Validate" MinWidth="92" Margin="0,6" />
            <Button Grid.Column="6" Name="SaveButton" Content="Save" MinWidth="82" Margin="0,6" />
            <Button Grid.Column="8" Name="FormButton" Content="Form" MinWidth="110" Margin="0,6" />
            <Button Grid.Column="9" Name="DefinitionButton" Content="Definition" MinWidth="110" Margin="0,6,8,6" />
        </Grid>

        <Grid Grid.Row="1" ColumnDefinitions="280,*,340" RowDefinitions="*,260">
            <Border Grid.Column="0" Grid.RowSpan="2" Background="#FFFFFF" BorderBrush="#CBD5E1" BorderThickness="0,0,1,0" Padding="8">
                <Grid RowDefinitions="Auto,Auto,*" RowSpacing="8">
                    <TextBox Name="PaletteSearchBox" Grid.Row="0" Watermark="Search" />
                    <TabControl Name="PaletteTabs" Grid.Row="1" Grid.RowSpan="2">
                        <TabItem Header="Actions">
                            <ListBox Name="ActionPaletteList" />
                        </TabItem>
                        <TabItem Header="Flow">
                            <ListBox Name="FlowPaletteList" />
                        </TabItem>
                    </TabControl>
                </Grid>
            </Border>

            <Border Grid.Column="1" Grid.Row="0" Background="#FFFFFF" BorderBrush="#CBD5E1" BorderThickness="0,0,1,1">
                <ScrollViewer HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Auto">
                    <local:QuestGraphCanvas Name="GraphCanvas" Width="1800" Height="1400" />
                </ScrollViewer>
            </Border>

            <Border Grid.Column="2" Grid.Row="0" Background="#FFFFFF" BorderBrush="#CBD5E1" BorderThickness="0,0,0,1" Padding="12">
                <Grid RowDefinitions="Auto,*" RowSpacing="10">
                    <TextBlock Name="InspectorTitleText" Grid.Row="0" FontSize="18" FontWeight="SemiBold" Text="Workflow" />
                    <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
                        <StackPanel Name="InspectorPanel" Spacing="8" />
                    </ScrollViewer>
                </Grid>
            </Border>

            <TabControl Grid.Column="1" Grid.ColumnSpan="2" Grid.Row="1" Name="BottomTabs" Background="#FFFFFF">
                <TabItem Header="Diagnostics">
                    <ListBox Name="DiagnosticsList" Classes="mono-list" />
                </TabItem>
                <TabItem Header="Walkthrough">
                    <TextBox Name="WalkthroughBox" Classes="mono-box" IsReadOnly="True" AcceptsReturn="True" TextWrapping="Wrap" />
                </TabItem>
                <TabItem Header="Lua Preview">
                    <TextBox Name="LuaPreviewBox" Classes="mono-box" IsReadOnly="True" AcceptsReturn="True" TextWrapping="NoWrap" />
                </TabItem>
                <TabItem Header="SQL Preview">
                    <TextBox Name="SqlPreviewBox" Classes="mono-box" IsReadOnly="True" AcceptsReturn="True" TextWrapping="NoWrap" />
                </TabItem>
                <TabItem Header="Missing Report">
                    <TextBox Name="MissingReportBox" Classes="mono-box" IsReadOnly="True" AcceptsReturn="True" TextWrapping="Wrap" />
                </TabItem>
                <TabItem Header="Generation Log">
                    <ListBox Name="GenerationLogList" Classes="mono-list" />
                </TabItem>
            </TabControl>
        </Grid>
    </Grid>
</Window>
```

- [ ] **Step 2: Add editor window code-behind**

Create `src/QuestParser.Desktop/VisualEditorWindow.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using QuestParser.Core;

namespace QuestParser.Desktop;

public partial class VisualEditorWindow : Window
{
    private readonly VisualEditorViewModel _viewModel;
    private readonly bool _ownsSpec;

    public VisualEditorWindow()
        : this(new QuestWorkflow(), null, ownsSpec: true)
    {
    }

    public VisualEditorWindow(QuestWorkflow workflow, QuestSpec? spec, bool ownsSpec)
    {
        InitializeComponent();
        _viewModel = new VisualEditorViewModel(workflow, spec);
        _ownsSpec = ownsSpec;
        PopulatePalette();
        WireEvents();
        RefreshAll();
    }

    public QuestSpec? EditedSpec => _viewModel.Spec;

    private void WireEvents()
    {
        GraphCanvas.NodeSelected += nodeId =>
        {
            _viewModel.SelectNode(nodeId);
            RefreshInspector();
            RefreshCanvas();
        };
        GraphCanvas.NodeMoved += (nodeId, x, y) =>
        {
            _viewModel.MoveNode(nodeId, x, y);
            RefreshCanvas();
        };
        ValidateButton.Click += (_, _) => RefreshDiagnostics();
        SaveButton.Click += async (_, _) => await SaveAsync();
        ActionPaletteList.DoubleTapped += (_, _) => AddSelectedAction();
        FlowPaletteList.DoubleTapped += (_, _) => AddSelectedFlow();
        FormButton.Click += (_, _) => RefreshInspector();
        DefinitionButton.Click += (_, _) => ShowDefinition();
    }

    private void PopulatePalette()
    {
        ActionPaletteList.ItemsSource = Enum.GetValues<StepType>()
            .Select(type => new PaletteItem(type.ToString(), type))
            .ToArray();
        FlowPaletteList.ItemsSource = new[]
        {
            new PaletteItem("Stage", null),
            new PaletteItem("Parallel Stage", null),
            new PaletteItem("Random Options", null),
            new PaletteItem("Comment", null)
        };
    }

    private void AddSelectedAction()
    {
        if (ActionPaletteList.SelectedItem is not PaletteItem item || item.StepType is null)
            return;

        var stageIndex = _viewModel.SelectedNode?.StageIndex ?? 0;
        _viewModel.AddStep(stageIndex, item.StepType.Value);
        RefreshAll();
    }

    private void AddSelectedFlow()
    {
        if (FlowPaletteList.SelectedItem is not PaletteItem item)
            return;

        if (item.Label == "Stage")
            _viewModel.AddStage(isParallel: false);
        else if (item.Label == "Parallel Stage")
            _viewModel.AddStage(isParallel: true);
        RefreshAll();
    }

    private async Task SaveAsync()
    {
        await _viewModel.SaveSpecAsync();
        RefreshAll();
        if (!_ownsSpec)
            Close(_viewModel.Spec);
    }

    private void RefreshAll()
    {
        _viewModel.RefreshPreview();
        RefreshCanvas();
        RefreshDiagnostics();
        RefreshInspector();
        WalkthroughBox.Text = _viewModel.Walkthrough;
        LuaPreviewBox.Text = _viewModel.LuaPreview;
        SqlPreviewBox.Text = _viewModel.SqlPreview;
        MissingReportBox.Text = _viewModel.MissingPreview;
        GenerationLogList.ItemsSource = _viewModel.GenerationLog;
    }

    private void RefreshCanvas()
    {
        GraphCanvas.Graph = _viewModel.Graph;
        GraphCanvas.SelectedNodeId = _viewModel.SelectedNode?.Id ?? "";
    }

    private void RefreshDiagnostics()
    {
        _viewModel.Validate();
        DiagnosticsList.ItemsSource = _viewModel.Diagnostics;
    }

    private void RefreshInspector()
    {
        InspectorPanel.Children.Clear();
        var selected = _viewModel.SelectedNode;
        InspectorTitleText.Text = selected?.Title ?? "Workflow";
        AddReadOnlyRow("Selected", selected?.Id ?? "Quest");
        AddReadOnlyRow("Kind", selected?.Kind.ToString() ?? "Quest");
        AddReadOnlyRow("Details", selected?.Subtitle ?? _viewModel.Spec?.Quest.Name ?? "");
    }

    private void ShowDefinition()
    {
        InspectorPanel.Children.Clear();
        InspectorTitleText.Text = "Definition";
        var box = new TextBox
        {
            Text = _viewModel.Definition,
            AcceptsReturn = true,
            IsReadOnly = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MinHeight = 420
        };
        InspectorPanel.Children.Add(box);
    }

    private void AddReadOnlyRow(string label, string value)
    {
        InspectorPanel.Children.Add(new TextBlock { Text = label, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        InspectorPanel.Children.Add(new TextBox { Text = value, IsReadOnly = true });
    }

    private sealed record PaletteItem(string Label, StepType? StepType)
    {
        public override string ToString() => Label;
    }
}
```

- [ ] **Step 3: Build desktop project**

Run:

```powershell
dotnet build src\QuestParser.Desktop\QuestParser.Desktop.csproj --no-restore
```

Expected: PASS.

- [ ] **Step 4: Commit**

Run:

```powershell
git add src\QuestParser.Desktop\VisualEditorWindow.axaml src\QuestParser.Desktop\VisualEditorWindow.axaml.cs
git commit -m "Add visual editor window shell"
```

---

### Task 8: Add Standalone Desktop Launch Mode

**Files:**
- Modify: `src/QuestParser.Desktop/App.axaml.cs`

- [ ] **Step 1: Modify app startup**

Replace `src/QuestParser.Desktop/App.axaml.cs` with:

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using QuestParser.Core;

namespace QuestParser.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = ShouldOpenVisualEditor(desktop.Args)
                ? new VisualEditorWindow(CreateWorkflow(), LoadSpecFromArgs(desktop.Args), ownsSpec: true)
                : new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static bool ShouldOpenVisualEditor(string[]? args)
    {
        return args?.Any(arg => string.Equals(arg, "--visual-editor", StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static QuestSpec? LoadSpecFromArgs(string[]? args)
    {
        if (args is null)
            return null;

        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], "--spec", StringComparison.OrdinalIgnoreCase))
                continue;

            var path = args[i + 1];
            if (File.Exists(path))
                return QuestWorkflow.ReadSpecAsync(path).GetAwaiter().GetResult();
        }

        return null;
    }

    private static QuestWorkflow CreateWorkflow()
    {
        var settings = QuestParserUiSettings.Load();
        return new QuestWorkflow(
            censusClient: CensusClientFactory.Create(settings.ToCensusOptions()),
            resolver: settings.CreateDatabaseResolver());
    }
}
```

- [ ] **Step 2: Build desktop project**

Run:

```powershell
dotnet build src\QuestParser.Desktop\QuestParser.Desktop.csproj --no-restore
```

Expected: PASS.

- [ ] **Step 3: Smoke launch visual editor**

Run:

```powershell
dotnet run --project src\QuestParser.Desktop -- --visual-editor
```

Expected: The Avalonia app opens directly to `EQ2Emu Quest Visual Editor`. Close the window manually after confirming it opens.

- [ ] **Step 4: Commit**

Run:

```powershell
git add src\QuestParser.Desktop\App.axaml.cs
git commit -m "Add standalone visual editor launch"
```

---

### Task 9: Add Integrated Launch From MainWindow

**Files:**
- Modify: `src/QuestParser.Desktop/MainWindow.axaml`
- Modify: `src/QuestParser.Desktop/MainWindow.axaml.cs`

- [ ] **Step 1: Add menu item to MainWindow XAML**

In `src/QuestParser.Desktop/MainWindow.axaml`, replace the current menu block:

```xml
<Menu Grid.Row="0">
    <MenuItem Header="_File">
        <MenuItem Name="SettingsMenuItem" Header="_Settings..." />
    </MenuItem>
    <MenuItem Header="_View">
        <MenuItem Name="LayoutSettingsMenuItem" Header="_Layout and visibility..." />
    </MenuItem>
</Menu>
```

with:

```xml
<Menu Grid.Row="0">
    <MenuItem Header="_File">
        <MenuItem Name="SettingsMenuItem" Header="_Settings..." />
    </MenuItem>
    <MenuItem Header="_Tools">
        <MenuItem Name="VisualEditorMenuItem" Header="_Open Visual Editor..." />
    </MenuItem>
    <MenuItem Header="_View">
        <MenuItem Name="LayoutSettingsMenuItem" Header="_Layout and visibility..." />
    </MenuItem>
</Menu>
```

- [ ] **Step 2: Wire menu click in constructor**

In `src/QuestParser.Desktop/MainWindow.axaml.cs`, add this line in the constructor near the other menu event registrations:

```csharp
VisualEditorMenuItem.Click += async (_, _) => await OpenVisualEditorAsync();
```

The top constructor event registration group should include:

```csharp
SettingsMenuItem.Click += async (_, _) => await OpenSettingsAsync();
VisualEditorMenuItem.Click += async (_, _) => await OpenVisualEditorAsync();
LayoutSettingsMenuItem.Click += async (_, _) => await OpenSettingsAsync();
```

- [ ] **Step 3: Add OpenVisualEditorAsync method**

In `src/QuestParser.Desktop/MainWindow.axaml.cs`, add this method near the other workflow action methods:

```csharp
private async Task OpenVisualEditorAsync()
{
    if (_spec is null)
    {
        SetStatus("Load, import, or create a quest before opening the visual editor.");
        AppendLog("Visual editor requires a loaded quest spec.");
        return;
    }

    SaveCurrentSection();
    ApplySettingsGenerationModeToSpec();

    var editor = new VisualEditorWindow(_workflow, _spec, ownsSpec: false);
    var result = await editor.ShowDialog<QuestSpec?>(this);
    if (result is null)
        return;

    _spec = result;
    QuestNameBox.Text = _spec.Quest.Name;
    AuthorBox.Text = _spec.Quest.Author;
    SpecPathBox.Text = _spec.Output.SpecPath;
    RebuildSections();
    SetWorkflowEnabled(true);
    SelectSection(0);
    RefreshPreview();
    AppendLog("Visual editor changes returned to the QuestParser review window.");
}
```

- [ ] **Step 4: Enable menu item only when not busy**

In `SetBusy` or the existing button enablement method in `MainWindow.axaml.cs`, add:

```csharp
VisualEditorMenuItem.IsEnabled = !_busy;
```

If there is no single obvious enablement block for menu items, add the line next to existing `SettingsMenuItem.IsEnabled` and `LayoutSettingsMenuItem.IsEnabled` assignments.

- [ ] **Step 5: Build desktop project**

Run:

```powershell
dotnet build src\QuestParser.Desktop\QuestParser.Desktop.csproj --no-restore
```

Expected: PASS.

- [ ] **Step 6: Commit**

Run:

```powershell
git add src\QuestParser.Desktop\MainWindow.axaml src\QuestParser.Desktop\MainWindow.axaml.cs
git commit -m "Open visual editor from desktop review window"
```

---

### Task 10: Add Editable Inspector Fields

**Files:**
- Modify: `src/QuestParser.Desktop/VisualEditorWindow.axaml.cs`

- [ ] **Step 1: Replace read-only inspector with editable controls**

In `src/QuestParser.Desktop/VisualEditorWindow.axaml.cs`, replace the `RefreshInspector` method with:

```csharp
private void RefreshInspector()
{
    InspectorPanel.Children.Clear();
    var spec = _viewModel.Spec;
    var selected = _viewModel.SelectedNode;
    InspectorTitleText.Text = selected?.Title ?? "Workflow";

    if (spec is null)
    {
        AddReadOnlyRow("State", "No quest loaded");
        return;
    }

    if (selected?.StageIndex is int stageIndex && selected.StepIndex is int stepIndex)
    {
        var step = spec.Stages[stageIndex].Steps[stepIndex];
        AddEditableRow("Description", step.Description, value =>
        {
            step.Description = value;
            _viewModel.RefreshPreview();
            RefreshAll();
        });
        AddEditableRow("Completed", step.CompletedDescription, value =>
        {
            step.CompletedDescription = value;
            _viewModel.RefreshPreview();
            RefreshAll();
        });
        AddEditableRow("Search", step.SearchText, value =>
        {
            step.SearchText = value;
            step.Target.Query = value;
            _viewModel.RefreshPreview();
            RefreshAll();
        });
        AddEditableIntRow("Quantity", step.QuantityMax, value =>
        {
            step.QuantityMax = value;
            _viewModel.RefreshPreview();
            RefreshAll();
        });
        return;
    }

    if (selected?.StageIndex is int selectedStageIndex)
    {
        var stage = spec.Stages[selectedStageIndex];
        AddEditableRow("Stage text", stage.Description, value =>
        {
            stage.Description = value;
            _viewModel.RefreshPreview();
            RefreshAll();
        });
        AddEditableRow("Completed text", stage.CompletedDescription, value =>
        {
            stage.CompletedDescription = value;
            _viewModel.RefreshPreview();
            RefreshAll();
        });
        AddReadOnlyRow("Parallel", stage.IsParallel ? "Yes" : "No");
        return;
    }

    AddEditableRow("Quest name", spec.Quest.Name, value =>
    {
        spec.Quest.Name = value;
        _viewModel.RefreshPreview();
        RefreshAll();
    });
    AddEditableRow("Zone", spec.Quest.Zone, value =>
    {
        spec.Quest.Zone = value;
        _viewModel.RefreshPreview();
        RefreshAll();
    });
    AddEditableRow("Starter text", spec.Quest.StarterText, value =>
    {
        spec.Quest.StarterText = value;
        _viewModel.RefreshPreview();
        RefreshAll();
    });
    AddEditableRow("Completion text", spec.Quest.CompletionText, value =>
    {
        spec.Quest.CompletionText = value;
        _viewModel.RefreshPreview();
        RefreshAll();
    });
}
```

Add these helper methods below `AddReadOnlyRow`:

```csharp
private void AddEditableRow(string label, string value, Action<string> save)
{
    InspectorPanel.Children.Add(new TextBlock { Text = label, FontWeight = Avalonia.Media.FontWeight.SemiBold });
    var box = new TextBox { Text = value };
    box.LostFocus += (_, _) => save((box.Text ?? "").Trim());
    InspectorPanel.Children.Add(box);
}

private void AddEditableIntRow(string label, int value, Action<int> save)
{
    InspectorPanel.Children.Add(new TextBlock { Text = label, FontWeight = Avalonia.Media.FontWeight.SemiBold });
    var box = new TextBox { Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture) };
    box.LostFocus += (_, _) =>
    {
        if (int.TryParse(box.Text, out var parsed) && parsed > 0)
            save(parsed);
    };
    InspectorPanel.Children.Add(box);
}
```

- [ ] **Step 2: Build desktop project**

Run:

```powershell
dotnet build src\QuestParser.Desktop\QuestParser.Desktop.csproj --no-restore
```

Expected: PASS.

- [ ] **Step 3: Commit**

Run:

```powershell
git add src\QuestParser.Desktop\VisualEditorWindow.axaml.cs
git commit -m "Add editable visual editor inspector"
```

---

### Task 11: Add Save, Generate, And File Open Actions

**Files:**
- Modify: `src/QuestParser.Desktop/VisualEditorWindow.axaml`
- Modify: `src/QuestParser.Desktop/VisualEditorWindow.axaml.cs`

- [ ] **Step 1: Add Open and Generate buttons to toolbar**

In `src/QuestParser.Desktop/VisualEditorWindow.axaml`, change the toolbar `ColumnDefinitions` to:

```xml
ColumnDefinitions="Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,*,Auto,Auto"
```

Add these buttons after `ValidateButton`:

```xml
<Button Grid.Column="7" Name="OpenButton" Content="Open" MinWidth="82" Margin="0,6" />
<Button Grid.Column="8" Name="GenerateButton" Content="Generate" MinWidth="96" Margin="0,6" />
```

Move `FormButton` and `DefinitionButton` to columns `10` and `11`.

- [ ] **Step 2: Wire Open and Generate**

In `VisualEditorWindow.axaml.cs`, add these event registrations in `WireEvents`:

```csharp
OpenButton.Click += async (_, _) => await OpenSpecAsync();
GenerateButton.Click += async (_, _) => await GenerateAsync();
```

Add these methods:

```csharp
private async Task OpenSpecAsync()
{
    var files = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
    {
        AllowMultiple = false,
        Title = "Open quest spec",
        FileTypeFilter =
        [
            new Avalonia.Platform.Storage.FilePickerFileType("Quest spec")
            {
                Patterns = ["*.quest.json"]
            }
        ]
    });

    var file = files.FirstOrDefault();
    if (file?.Path.LocalPath is not string path || !File.Exists(path))
        return;

    var spec = await QuestWorkflow.ReadSpecAsync(path);
    _viewModel.LoadSpec(spec);
    RefreshAll();
}

private async Task GenerateAsync()
{
    var result = await _viewModel.GenerateAsync(overwrite: true);
    if (result is null)
        return;

    foreach (var file in result.WrittenFiles)
        _viewModel.GenerationLog.Add("Generated " + file);
    RefreshAll();
}
```

- [ ] **Step 3: Build desktop project**

Run:

```powershell
dotnet build src\QuestParser.Desktop\QuestParser.Desktop.csproj --no-restore
```

Expected: PASS.

- [ ] **Step 4: Commit**

Run:

```powershell
git add src\QuestParser.Desktop\VisualEditorWindow.axaml src\QuestParser.Desktop\VisualEditorWindow.axaml.cs
git commit -m "Add visual editor file and generation actions"
```

---

### Task 12: Add Documentation And Final Verification

**Files:**
- Modify: `README.md`
- Modify: `QUEST_PARSER_USER_GUIDE.md`

- [ ] **Step 1: Update README commands**

In `README.md`, under the UI command block, add:

````markdown
Run the visual editor as the first window:

```powershell
dotnet run --project src\QuestParser.Desktop -- --visual-editor
dotnet run --project src\QuestParser.Desktop -- --visual-editor --spec ".\eq2emu-content\Quests\Commonlands\a_hunters_tool.quest.json"
```
````

Under "Generated files", add:

```markdown
Visual editor layout is stored inside the `.quest.json` spec under `visualEditor`. The quest spec remains the only quest-data file; no separate graph project file is required.
```

- [ ] **Step 2: Update user guide workflow**

In `QUEST_PARSER_USER_GUIDE.md`, after the recommended workflow list, add:

```markdown
## Visual Editor Workflow

The visual editor opens as a separate full-size window. From the desktop review UI, load or create a quest, then choose `Tools > Open Visual Editor...`. You can also launch the desktop app directly into the visual editor with `--visual-editor`.

The editor uses the existing `.quest.json` spec as the source of truth. Graph positions, zoom, and review state are stored inside that spec under `visualEditor`. Sequential stages, parallel stages, random-option steps, and all current QuestParser step types round-trip through the same generation pipeline used by the review UI and CLI.

The first implementation intentionally follows current QuestParser generation semantics. It does not add arbitrary conditional branches, loops, or failure paths.
```

- [ ] **Step 3: Run full test suite**

Run:

```powershell
dotnet test --no-restore
```

Expected: PASS. The total test count should include the existing 59 tests plus the new visual editor core tests.

- [ ] **Step 4: Run full build**

Run:

```powershell
dotnet build --no-restore
```

Expected: PASS for Core, CLI, Desktop, WinForms, and tests.

- [ ] **Step 5: Smoke launch desktop review window**

Run:

```powershell
dotnet run --project src\QuestParser.Desktop
```

Expected: Existing QuestParser desktop review window opens. Confirm `Tools > Open Visual Editor...` exists, then close the app.

- [ ] **Step 6: Smoke launch standalone visual editor**

Run:

```powershell
dotnet run --project src\QuestParser.Desktop -- --visual-editor
```

Expected: Visual editor window opens directly. Confirm the palette, canvas, inspector, and bottom tabs are visible, then close the app.

- [ ] **Step 7: Commit**

Run:

```powershell
git add README.md QUEST_PARSER_USER_GUIDE.md
git commit -m "Document quest visual editor workflow"
```

---

## Final Verification

After all tasks are complete, run:

```powershell
dotnet test --no-restore
dotnet build --no-restore
git status --short --branch
```

Expected:

- Tests pass.
- Build passes.
- Working tree is clean.
- Branch contains the task commits.
