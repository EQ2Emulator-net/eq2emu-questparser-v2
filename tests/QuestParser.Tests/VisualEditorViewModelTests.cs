using QuestParser.Core;
using QuestParser.Desktop;

namespace QuestParser.Tests;

public sealed class VisualEditorViewModelTests
{
    [Fact]
    public void LoadSpecClearsPreviousPreviewAndGenerationLog()
    {
        const string firstQuestName = "First Visual Quest";
        var viewModel = new VisualEditorViewModel(new QuestWorkflow());
        viewModel.LoadSpec(CreateQuestSpec(firstQuestName));
        Assert.Contains(firstQuestName, viewModel.LuaPreview, StringComparison.Ordinal);

        viewModel.GenerationLog.Add("old generation log entry");

        viewModel.LoadSpec(CreateBlockedQuestSpec("Blocked Visual Quest"));

        Assert.DoesNotContain(firstQuestName, viewModel.LuaPreview, StringComparison.Ordinal);
        Assert.DoesNotContain(firstQuestName, viewModel.SqlPreview, StringComparison.Ordinal);
        Assert.DoesNotContain(firstQuestName, viewModel.MissingPreview, StringComparison.Ordinal);
        Assert.DoesNotContain("old generation log entry", viewModel.GenerationLog);
    }

    [Fact]
    public void AddStepWithInvalidStageIndexDoesNotThrowOrMarkDirty()
    {
        var viewModel = new VisualEditorViewModel(new QuestWorkflow(), CreateBlockedQuestSpec("No Stage Quest"));

        var exception = Record.Exception(() => viewModel.AddStep(0, StepType.Kill));

        Assert.Null(exception);
        Assert.False(viewModel.IsDirty);
        Assert.Contains(
            viewModel.GenerationLog,
            entry => entry.Contains("stage index", StringComparison.OrdinalIgnoreCase)
                && entry.Contains("0", StringComparison.Ordinal));
    }

    [Fact]
    public void MoveNodePersistsCoordinatesToSpecVisualEditorState()
    {
        var spec = CreateQuestSpec("Move Node Quest");
        var viewModel = new VisualEditorViewModel(new QuestWorkflow(), spec);
        var nodeId = Assert.Single(viewModel.Graph.Nodes, node => node.Kind == QuestGraphNodeKind.Stage).Id;

        viewModel.MoveNode(nodeId, 123.5, 456.25);

        Assert.True(viewModel.IsDirty);
        Assert.NotNull(spec.VisualEditor);
        var layout = Assert.Single(spec.VisualEditor.Nodes, node => node.Id == nodeId);
        Assert.Equal(123.5, layout.X);
        Assert.Equal(456.25, layout.Y);
    }

    [Fact]
    public void SetStageParallelUpdatesSpecAndMarksDirty()
    {
        var spec = CreateQuestSpec("Parallel Stage Quest");
        var viewModel = new VisualEditorViewModel(new QuestWorkflow(), spec);

        viewModel.SetStageParallel(stageIndex: 0, isParallel: true);

        Assert.True(spec.Stages[0].IsParallel);
        Assert.True(viewModel.IsDirty);
        Assert.True(Assert.Single(viewModel.Graph.Nodes, node => node.Kind == QuestGraphNodeKind.Stage).IsParallelStage);
    }

    [Fact]
    public void MoveStepToStageMovesStepRenumbersAndMarksDirty()
    {
        var spec = CreateQuestSpec("Move Step Quest");
        spec.Stages.Add(new QuestStageSpec
        {
            Number = 2,
            Description = "Second stage",
            CompletedDescription = "Second stage complete"
        });
        var movedStep = spec.Stages[0].Steps[0];
        var viewModel = new VisualEditorViewModel(new QuestWorkflow(), spec);

        viewModel.MoveStepToStage(fromStageIndex: 0, fromStepIndex: 0, toStageIndex: 1);

        Assert.Empty(spec.Stages[0].Steps);
        Assert.Same(movedStep, Assert.Single(spec.Stages[1].Steps));
        Assert.Equal(1, movedStep.Number);
        Assert.True(viewModel.IsDirty);
    }

    [Fact]
    public void DeleteNodeRemovesSelectedStep()
    {
        var spec = CreateTwoStageQuestSpec("Delete Step Quest");
        var removedStep = spec.Stages[0].Steps[0];
        var viewModel = new VisualEditorViewModel(new QuestWorkflow(), spec);
        var nodeId = StepNodeId(viewModel, stageIndex: 0, stepIndex: 0);

        var deleted = viewModel.DeleteNode(nodeId);

        Assert.True(deleted);
        Assert.DoesNotContain(removedStep, spec.Stages.SelectMany(stage => stage.Steps));
        Assert.Equal("Step B", Assert.Single(spec.Stages[0].Steps).Description);
        Assert.True(viewModel.IsDirty);
    }

    [Fact]
    public void DeleteNodeRemovesSelectedStage()
    {
        var spec = CreateTwoStageQuestSpec("Delete Stage Quest");
        var viewModel = new VisualEditorViewModel(new QuestWorkflow(), spec);
        var nodeId = StageNodeId(viewModel, stageIndex: 0);

        var deleted = viewModel.DeleteNode(nodeId);

        Assert.True(deleted);
        var stage = Assert.Single(spec.Stages);
        Assert.Equal("Second stage", stage.Description);
        Assert.Equal(1, stage.Number);
        Assert.Equal(1, Assert.Single(stage.Steps).Number);
        Assert.True(viewModel.IsDirty);
    }

    [Fact]
    public void DeleteNodeDoesNotDeleteStartOrCompleteNodes()
    {
        var spec = CreateTwoStageQuestSpec("Delete Protected Node Quest");
        var viewModel = new VisualEditorViewModel(new QuestWorkflow(), spec);

        Assert.False(viewModel.DeleteNode("start"));
        Assert.False(viewModel.DeleteNode("complete"));
        Assert.Equal(2, spec.Stages.Count);
        Assert.False(viewModel.IsDirty);
    }

    [Fact]
    public void ConnectStepToStageMovesStepToEndOfTargetStage()
    {
        var spec = CreateTwoStageQuestSpec("Connect Step To Stage Quest");
        var movedStep = spec.Stages[0].Steps[0];
        var viewModel = new VisualEditorViewModel(new QuestWorkflow(), spec);
        var sourceNodeId = StepNodeId(viewModel, stageIndex: 0, stepIndex: 0);
        var targetNodeId = StageNodeId(viewModel, stageIndex: 1);

        var connected = viewModel.ConnectNodes(sourceNodeId, targetNodeId);

        Assert.True(connected);
        Assert.Equal(["Step B"], spec.Stages[0].Steps.Select(step => step.Description));
        Assert.Equal(["Step C", "Step A"], spec.Stages[1].Steps.Select(step => step.Description));
        Assert.Same(movedStep, spec.Stages[1].Steps[1]);
        Assert.True(viewModel.IsDirty);
    }

    [Fact]
    public void ConnectStepToStepMovesSourceAfterTarget()
    {
        var spec = CreateTwoStageQuestSpec("Connect Step To Step Quest");
        var movedStep = spec.Stages[0].Steps[0];
        var viewModel = new VisualEditorViewModel(new QuestWorkflow(), spec);
        var sourceNodeId = StepNodeId(viewModel, stageIndex: 0, stepIndex: 0);
        var targetNodeId = StepNodeId(viewModel, stageIndex: 1, stepIndex: 0);

        var connected = viewModel.ConnectNodes(sourceNodeId, targetNodeId);

        Assert.True(connected);
        Assert.Equal(["Step B"], spec.Stages[0].Steps.Select(step => step.Description));
        Assert.Equal(["Step C", "Step A"], spec.Stages[1].Steps.Select(step => step.Description));
        Assert.Same(movedStep, spec.Stages[1].Steps[1]);
    }

    [Fact]
    public void ConnectStageToStageMovesSourceAfterTarget()
    {
        var spec = CreateTwoStageQuestSpec("Connect Stage To Stage Quest");
        var movedStage = spec.Stages[0];
        var viewModel = new VisualEditorViewModel(new QuestWorkflow(), spec);
        var sourceNodeId = StageNodeId(viewModel, stageIndex: 0);
        var targetNodeId = StageNodeId(viewModel, stageIndex: 1);

        var connected = viewModel.ConnectNodes(sourceNodeId, targetNodeId);

        Assert.True(connected);
        Assert.Equal(["Second stage", "First stage"], spec.Stages.Select(stage => stage.Description));
        Assert.Same(movedStage, spec.Stages[1]);
        Assert.Equal(1, spec.Stages[0].Number);
        Assert.Equal(2, spec.Stages[1].Number);
        Assert.True(viewModel.IsDirty);
    }

    [Fact]
    public void MarkDirtyMarksLoadedSpecDirty()
    {
        var viewModel = new VisualEditorViewModel(new QuestWorkflow(), CreateQuestSpec("Dirty Quest"));

        viewModel.MarkDirty();

        Assert.True(viewModel.IsDirty);
    }

    [Fact]
    public void DefinitionBuilderUsesGenericNodeDefinitionForStaleIndexes()
    {
        var spec = CreateBlockedQuestSpec("Stale Definition Quest");
        var staleNode = new QuestGraphNode
        {
            Id = "stale-step",
            Kind = QuestGraphNodeKind.Step,
            StageIndex = 0,
            StepIndex = 0,
            Title = "Stale title",
            Subtitle = "Stale subtitle"
        };

        var definition = VisualEditorDefinitionBuilder.Build(spec, staleNode);

        Assert.Contains("Node id: stale-step", definition, StringComparison.Ordinal);
        Assert.Contains("Title: Stale title", definition, StringComparison.Ordinal);
        Assert.DoesNotContain("Step number:", definition, StringComparison.Ordinal);
    }

    private static QuestSpec CreateQuestSpec(string questName)
    {
        return new QuestSpec
        {
            Quest = new QuestMetadata
            {
                Name = questName,
                Zone = "Antonica",
                StarterText = "Start " + questName,
                CompletionText = "Complete " + questName
            },
            Output = new OutputPaths
            {
                ContentRoot = "E:\\_EQ2\\eq2emu-content",
                QuestDirectory = "E:\\_EQ2\\eq2emu-content\\Quests\\Antonica",
                LuaPath = "E:\\_EQ2\\eq2emu-content\\Quests\\Antonica\\" + questName + ".lua",
                SpecPath = "E:\\_EQ2\\eq2emu-content\\Quests\\Antonica\\" + questName + ".quest.json",
                SqlPath = "E:\\_EQ2\\eq2emu-content\\Quests\\Antonica\\" + questName + ".sql",
                MissingReportPath = "E:\\_EQ2\\eq2emu-content\\Quests\\Antonica\\" + questName + ".missing.md"
            },
            QuestId = ResolvedReference.Resolved("quest", questName, 1001, questName),
            Giver = ResolvedReference.Resolved("npc", "Quest Giver", 2001, "Quest Giver"),
            Stages =
            [
                new QuestStageSpec
                {
                    Number = 1,
                    Description = "Do the work",
                    CompletedDescription = "Work complete",
                    Steps =
                    [
                        new QuestStepSpec
                        {
                            Number = 1,
                            Type = StepType.Generic,
                            Description = "Generic objective",
                            CompletedDescription = "Generic objective complete",
                            QuantityMin = 0,
                            QuantityMax = 1
                        }
                    ]
                }
            ]
        };
    }

    private static QuestSpec CreateBlockedQuestSpec(string questName)
    {
        return new QuestSpec
        {
            Quest = new QuestMetadata
            {
                Name = questName,
                Zone = "Antonica",
                StarterText = "Start " + questName,
                CompletionText = "Complete " + questName
            }
        };
    }

    private static QuestSpec CreateTwoStageQuestSpec(string questName)
    {
        var spec = CreateQuestSpec(questName);
        spec.Stages[0].Description = "First stage";
        spec.Stages[0].Steps =
        [
            CreateStep(1, "Step A"),
            CreateStep(2, "Step B")
        ];
        spec.Stages.Add(new QuestStageSpec
        {
            Number = 2,
            Description = "Second stage",
            CompletedDescription = "Second stage complete",
            Steps =
            [
                CreateStep(3, "Step C")
            ]
        });
        return spec;
    }

    private static QuestStepSpec CreateStep(int number, string description)
    {
        return new QuestStepSpec
        {
            Number = number,
            Type = StepType.Kill,
            Description = description,
            CompletedDescription = description + " complete",
            QuantityMax = 1,
            Target = ResolvedReference.Resolved("npc", description, 1000 + number, description)
        };
    }

    private static string StageNodeId(VisualEditorViewModel viewModel, int stageIndex)
    {
        return Assert.Single(viewModel.Graph.Nodes, node => node.Kind == QuestGraphNodeKind.Stage && node.StageIndex == stageIndex).Id;
    }

    private static string StepNodeId(VisualEditorViewModel viewModel, int stageIndex, int stepIndex)
    {
        return Assert.Single(
            viewModel.Graph.Nodes,
            node => node.Kind == QuestGraphNodeKind.Step && node.StageIndex == stageIndex && node.StepIndex == stepIndex).Id;
    }
}
