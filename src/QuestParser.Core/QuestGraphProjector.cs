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
        var usedNodeIds = new HashSet<string>(StringComparer.Ordinal);
        var order = 0;

        var start = CreateNode("start", QuestGraphNodeKind.Start, "Start", spec.Quest.Name);
        start.Layout = _layoutService.LayoutFor(spec, start, order++);
        graph.Nodes.Add(start);
        usedNodeIds.Add(start.Id);

        string previousExit = start.Id;
        foreach (var stage in spec.Stages.OrderBy(s => s.Number))
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
            usedNodeIds.Add(stageNode.Id);
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

                var childOrder = order++;
                for (var i = 0; i < stage.Steps.Count; i++)
                {
                    var stepNode = CreateStepNode(stage, stageIndex, stage.Steps[i], i, usedNodeIds);
                    stepNode.Layout = _layoutService.LayoutFor(spec, stepNode, childOrder, i, stage.Steps.Count);
                    graph.Nodes.Add(stepNode);
                    graph.Edges.Add(CreateEdge(stageNode.Id, stepNode.Id, "parallel"));
                    graph.Edges.Add(CreateEdge(stepNode.Id, joinNode.Id, "complete"));
                }

                joinNode.Layout = _layoutService.LayoutFor(spec, joinNode, order++);
                graph.Nodes.Add(joinNode);
                usedNodeIds.Add(joinNode.Id);
                previousExit = joinNode.Id;
            }
            else
            {
                string prior = stageNode.Id;
                for (var i = 0; i < stage.Steps.Count; i++)
                {
                    var stepNode = CreateStepNode(stage, stageIndex, stage.Steps[i], i, usedNodeIds);
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
        usedNodeIds.Add(complete.Id);
        graph.Edges.Add(CreateEdge(previousExit, complete.Id, ""));

        _layoutService.EnsureVisualState(spec, graph);
        return graph;
    }

    private static QuestGraphNode CreateStepNode(
        QuestStageSpec stage,
        int stageIndex,
        QuestStepSpec step,
        int stepIndex,
        HashSet<string> usedNodeIds)
    {
        var kind = step.HasRandomOptions ? QuestGraphNodeKind.RandomOptions : QuestGraphNodeKind.Step;
        return new QuestGraphNode
        {
            Id = CreateStepNodeId(stage, step, stepIndex, usedNodeIds),
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

    private static string CreateStepNodeId(
        QuestStageSpec stage,
        QuestStepSpec step,
        int stepIndex,
        HashSet<string> usedNodeIds)
    {
        var baseId = $"stage-{stage.Number}-step-{step.Number}";
        if (usedNodeIds.Add(baseId))
            return baseId;

        var suffix = stepIndex + 1;
        var candidate = $"{baseId}-{suffix}";
        while (!usedNodeIds.Add(candidate))
        {
            suffix++;
            candidate = $"{baseId}-{suffix}";
        }

        return candidate;
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
