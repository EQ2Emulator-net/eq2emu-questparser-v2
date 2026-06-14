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
