using QuestParser.Core;

namespace QuestParser.Tests;

public sealed class QuestGraphValidatorTests
{
    [Fact]
    public void ValidSequentialGraphHasNoBlockerDiagnostics()
    {
        var graph = Project(BuildSpec());

        var diagnostics = new QuestGraphValidator().Validate(graph);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void OneStepParallelStageProjectedGraphHasNoParallelJoinBlocker()
    {
        var spec = BuildSpec();
        spec.Stages[0].IsParallel = true;
        var graph = Project(spec);

        var diagnostics = new QuestGraphValidator().Validate(graph);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void MissingCompleteNodeYieldsCompleteCountDiagnostic()
    {
        var graph = Project(BuildSpec());
        graph.Nodes.RemoveAll(node => node.Kind == QuestGraphNodeKind.Complete);
        graph.Edges.RemoveAll(edge => edge.TargetNodeId == "complete" || edge.SourceNodeId == "complete");

        var diagnostics = new QuestGraphValidator().Validate(graph);

        AssertContainsBlocker(diagnostics, "GRAPH_COMPLETE_COUNT");
    }

    [Fact]
    public void ParallelStageMissingGeneratedJoinYieldsParallelJoinDiagnostic()
    {
        var graph = Project(BuildParallelSpec());
        graph.Nodes.RemoveAll(node => node.Id == "stage-1-join");
        graph.Edges.RemoveAll(edge => edge.TargetNodeId == "stage-1-join" || edge.SourceNodeId == "stage-1-join");

        var diagnostics = new QuestGraphValidator().Validate(graph);

        AssertContainsBlocker(diagnostics, "GRAPH_PARALLEL_JOIN");
    }

    [Fact]
    public void ParallelStageJoinWithoutBranchFanInYieldsParallelJoinDiagnostic()
    {
        var graph = Project(BuildParallelSpec());
        graph.Edges.RemoveAll(edge => edge.TargetNodeId == "stage-1-join");

        var diagnostics = new QuestGraphValidator().Validate(graph);

        AssertContainsBlocker(diagnostics, "GRAPH_PARALLEL_JOIN");
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("stage-number")]
    [InlineData("stage-index")]
    public void MalformedParallelStageJoinYieldsParallelJoinDiagnostic(string malformedField)
    {
        var graph = Project(BuildParallelSpec());
        var join = Assert.Single(graph.Nodes, node => node.Id == "stage-1-join");
        switch (malformedField)
        {
            case "kind":
                join.Kind = QuestGraphNodeKind.Step;
                break;
            case "stage-number":
                join.StageNumber = 99;
                break;
            case "stage-index":
                join.StageIndex = 99;
                break;
        }

        var diagnostics = new QuestGraphValidator().Validate(graph);

        AssertContainsBlocker(diagnostics, "GRAPH_PARALLEL_JOIN");
    }

    [Fact]
    public void GeneratedJoinWithMultipleOutgoingEdgesYieldsUnsupportedBranchDiagnostic()
    {
        var graph = Project(BuildParallelSpec());
        graph.Edges.Add(new QuestGraphEdge
        {
            Id = "stage-1-join->complete-extra",
            SourceNodeId = "stage-1-join",
            TargetNodeId = "complete",
            Label = "extra"
        });

        var diagnostics = new QuestGraphValidator().Validate(graph);

        AssertContainsBlocker(diagnostics, "GRAPH_UNSUPPORTED_BRANCH");
    }

    [Fact]
    public void ExtraOutgoingEdgeFromSequentialNodeYieldsUnsupportedBranchDiagnostic()
    {
        var graph = Project(BuildSpec());
        graph.Edges.Add(new QuestGraphEdge
        {
            Id = "stage-1-step-1->complete-extra",
            SourceNodeId = "stage-1-step-1",
            TargetNodeId = "complete",
            Label = "extra"
        });

        var diagnostics = new QuestGraphValidator().Validate(graph);

        AssertContainsBlocker(diagnostics, "GRAPH_UNSUPPORTED_BRANCH");
    }

    [Fact]
    public void EdgeReferencingMissingNodeYieldsDisconnectedEdgeDiagnostic()
    {
        var graph = Project(BuildSpec());
        graph.Edges.Add(new QuestGraphEdge
        {
            Id = "missing-node->stage-1",
            SourceNodeId = "missing-node",
            TargetNodeId = "stage-1"
        });

        var diagnostics = new QuestGraphValidator().Validate(graph);

        AssertContainsBlocker(diagnostics, "GRAPH_DISCONNECTED_EDGE");
    }

    [Fact]
    public void DuplicateNodeIdYieldsDuplicateNodeDiagnostic()
    {
        var graph = Project(BuildSpec());
        graph.Nodes.Add(new QuestGraphNode
        {
            Id = "stage-1-step-1",
            Kind = QuestGraphNodeKind.Step,
            StageNumber = 1,
            StepNumber = 99,
            Title = "Duplicate step"
        });

        var diagnostics = new QuestGraphValidator().Validate(graph);

        AssertContainsBlocker(diagnostics, "GRAPH_DUPLICATE_NODE");
    }

    [Fact]
    public void OrphanGeneratedStepYieldsUnreachableNodeDiagnostic()
    {
        var graph = Project(BuildSpec());
        graph.Nodes.Add(new QuestGraphNode
        {
            Id = "stage-99-step-1",
            Kind = QuestGraphNodeKind.Step,
            StageNumber = 99,
            StageIndex = 98,
            StepNumber = 1,
            Title = "Orphan generated step"
        });

        var diagnostics = new QuestGraphValidator().Validate(graph);

        AssertContainsBlocker(diagnostics, "GRAPH_UNREACHABLE_NODE");
    }

    [Fact]
    public void NodeUnableToReachCompleteYieldsIncompletePathDiagnostic()
    {
        var graph = Project(BuildSpec());
        graph.Edges.RemoveAll(edge => edge.TargetNodeId == "complete");

        var diagnostics = new QuestGraphValidator().Validate(graph);

        AssertContainsBlocker(diagnostics, "GRAPH_INCOMPLETE_PATH");
    }

    [Fact]
    public void BackEdgeCycleYieldsCycleDiagnostic()
    {
        var graph = Project(BuildSpec());
        graph.Edges.Add(new QuestGraphEdge
        {
            Id = "stage-2-step-2->stage-1",
            SourceNodeId = "stage-2-step-2",
            TargetNodeId = "stage-1"
        });

        var diagnostics = new QuestGraphValidator().Validate(graph);

        AssertContainsBlocker(diagnostics, "GRAPH_CYCLE");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrDuplicateStartNodeYieldsStartCountDiagnostic(bool duplicateStart)
    {
        var graph = Project(BuildSpec());
        if (duplicateStart)
        {
            graph.Nodes.Add(new QuestGraphNode
            {
                Id = "second-start",
                Kind = QuestGraphNodeKind.Start,
                Title = "Second start"
            });
        }
        else
        {
            graph.Nodes.RemoveAll(node => node.Kind == QuestGraphNodeKind.Start);
            graph.Edges.RemoveAll(edge => edge.SourceNodeId == "start" || edge.TargetNodeId == "start");
        }

        var diagnostics = new QuestGraphValidator().Validate(graph);

        AssertContainsBlocker(diagnostics, "GRAPH_START_COUNT");
    }

    [Fact]
    public void ValidParallelGraphHasNoParallelJoinBlocker()
    {
        var graph = Project(BuildParallelSpec());

        var diagnostics = new QuestGraphValidator().Validate(graph);

        Assert.Empty(diagnostics);
    }

    private static QuestGraph Project(QuestSpec spec)
    {
        return new QuestGraphProjector().Project(spec);
    }

    private static void AssertContainsBlocker(IEnumerable<QuestDiagnostic> diagnostics, string code)
    {
        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Severity == QuestDiagnosticSeverity.Blocker
                && diagnostic.Code == code);
    }

    private static QuestSpec BuildParallelSpec()
    {
        var spec = BuildSpec();
        spec.Stages[0].IsParallel = true;
        spec.Stages[0].Steps.Add(CreateStep(3, StepType.Chat, "Speak with the scout", "Spoke with the scout"));
        return spec;
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
                        CreateStep(1, StepType.Kill, "Kill a wolf", "Killed a wolf")
                    ]
                },
                new QuestStageSpec
                {
                    Number = 2,
                    Description = "Second task group",
                    CompletedDescription = "Second task group complete",
                    Steps =
                    [
                        CreateStep(2, StepType.Chat, "Return to the guard", "Returned to the guard")
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
}
