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

        var parallelStage = Assert.Single(graph.Nodes, node => node.Id == "stage-1");
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
        var node = Assert.Single(graph.Nodes, node => node.Id == "stage-1-step-1");

        Assert.Equal(QuestGraphNodeKind.RandomOptions, node.Kind);
        Assert.Equal(1, node.RandomOptionCount);
    }

    [Fact]
    public void ProjectorDoesNotReusePersistedStageLayoutForParallelJoin()
    {
        var spec = BuildSpec();
        spec.Stages[0].IsParallel = true;
        spec.Stages[0].Steps.Add(CreateStep(3, StepType.Chat, "Speak with the scout", "Spoke with the scout"));
        spec.VisualEditor = new QuestVisualEditorState
        {
            Nodes =
            [
                new QuestGraphNodeLayout
                {
                    Id = "stage-1",
                    Kind = QuestGraphNodeKind.Stage,
                    StageNumber = 1,
                    X = 123,
                    Y = 456,
                    Width = 260,
                    Height = 54,
                    ReviewStatus = QuestVisualReviewStatus.Reviewed
                }
            ]
        };

        var graph = new QuestGraphProjector().Project(spec);

        var stage = Assert.Single(graph.Nodes, node => node.Id == "stage-1");
        var join = Assert.Single(graph.Nodes, node => node.Id == "stage-1-join");
        Assert.Equal("stage-1", stage.Layout.Id);
        Assert.Equal("stage-1-join", join.Layout.Id);
        Assert.NotSame(stage.Layout, join.Layout);
        Assert.Contains(spec.VisualEditor.Nodes, layout => layout.Id == "stage-1-join");
        Assert.Equal(spec.VisualEditor.Nodes.Count, spec.VisualEditor.Nodes.Select(layout => layout.Id).Distinct().Count());
    }

    [Fact]
    public void ProjectorNormalizesFallbackLayoutsToCurrentNodeId()
    {
        var originalLayout = new QuestGraphNodeLayout
        {
            Id = "legacy-stage-1-step-1",
            Kind = QuestGraphNodeKind.Step,
            StageNumber = 1,
            StepNumber = 1,
            X = 333,
            Y = 444,
            Width = 260,
            Height = 72,
            ReviewStatus = QuestVisualReviewStatus.Reviewed
        };
        var spec = BuildSpec();
        spec.VisualEditor = new QuestVisualEditorState
        {
            Nodes = [originalLayout]
        };

        var graph = new QuestGraphProjector().Project(spec);
        var step = Assert.Single(graph.Nodes, node => node.Id == "stage-1-step-1");

        Assert.Equal("stage-1-step-1", step.Layout.Id);
        Assert.NotSame(originalLayout, step.Layout);
        Assert.Equal(originalLayout.X, step.Layout.X);
        Assert.Equal(originalLayout.Y, step.Layout.Y);
        Assert.Contains(spec.VisualEditor.Nodes, layout => layout.Id == "stage-1-step-1");
    }

    [Fact]
    public void ProjectorPlacesOddCountParallelJoinBelowChildStepRow()
    {
        var spec = BuildSpec();
        spec.Stages[0].IsParallel = true;
        spec.Stages[0].Steps.Add(CreateStep(3, StepType.Chat, "Speak with the scout", "Spoke with the scout"));
        spec.Stages[0].Steps.Add(CreateStep(4, StepType.Kill, "Kill a beetle", "Killed a beetle"));

        var graph = new QuestGraphProjector().Project(spec);

        var join = Assert.Single(graph.Nodes, node => node.Id == "stage-1-join");
        var childSteps = graph.Nodes
            .Where(node => node.StageNumber == 1 && node.StepNumber is not null)
            .ToList();
        Assert.DoesNotContain(childSteps, child => child.Layout.Y == join.Layout.Y);

        var centerChild = Assert.Single(childSteps, child => child.Layout.X == join.Layout.X);
        Assert.False(LayoutsOverlap(centerChild.Layout, join.Layout));
    }

    [Fact]
    public void ProjectorDisambiguatesDuplicateStepNumbersWithinStage()
    {
        var spec = BuildSpec();
        spec.Stages[0].Steps.Add(CreateStep(1, StepType.Chat, "Speak with the scout", "Spoke with the scout"));

        var graph = new QuestGraphProjector().Project(spec);

        var duplicateNumberSteps = graph.Nodes
            .Where(node => node.StageNumber == 1 && node.StepNumber == 1)
            .ToList();

        Assert.Equal(2, duplicateNumberSteps.Count);
        Assert.Contains(duplicateNumberSteps, node => node.Id == "stage-1-step-1");
        Assert.Equal(duplicateNumberSteps.Count, duplicateNumberSteps.Select(node => node.Id).Distinct().Count());
        Assert.DoesNotContain(graph.Edges, edge => edge.SourceNodeId == edge.TargetNodeId);
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

    private static QuestStepSpec CreateStep(int number, StepType type, string description, string completedDescription)
    {
        return new QuestStepSpec
        {
            Number = number,
            Type = type,
            Description = description,
            CompletedDescription = completedDescription,
            QuantityMax = 1
        };
    }

    private static bool LayoutsOverlap(QuestGraphNodeLayout first, QuestGraphNodeLayout second)
    {
        return first.X < second.X + second.Width
            && first.X + first.Width > second.X
            && first.Y < second.Y + second.Height
            && first.Y + first.Height > second.Y;
    }
}
