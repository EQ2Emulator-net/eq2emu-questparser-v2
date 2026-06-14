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
}
