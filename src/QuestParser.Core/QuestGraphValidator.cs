namespace QuestParser.Core;

public sealed class QuestGraphValidator
{
    public List<QuestDiagnostic> Validate(QuestGraph graph)
    {
        var diagnostics = new List<QuestDiagnostic>();
        var nodeIds = graph.Nodes
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);
        var nodesById = graph.Nodes
            .GroupBy(node => node.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var outgoingTargetsBySource = graph.Edges
            .GroupBy(edge => edge.SourceNodeId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.TargetNodeId).ToList(), StringComparer.Ordinal);
        var incomingSourcesByTarget = graph.Edges
            .GroupBy(edge => edge.TargetNodeId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.SourceNodeId).ToList(), StringComparer.Ordinal);
        var validEdges = graph.Edges
            .Where(edge => nodeIds.Contains(edge.SourceNodeId) && nodeIds.Contains(edge.TargetNodeId))
            .ToList();
        var validOutgoingTargetsBySource = validEdges
            .GroupBy(edge => edge.SourceNodeId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.TargetNodeId).Distinct(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
        var validIncomingSourcesByTarget = validEdges
            .GroupBy(edge => edge.TargetNodeId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.SourceNodeId).Distinct(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);

        ValidateStartCount(graph, diagnostics);
        ValidateCompleteCount(graph, diagnostics);
        ValidateDuplicateNodeIds(nodesById, diagnostics);
        ValidateEdges(graph, nodeIds, diagnostics);
        var validGeneratedJoinIds = ValidateParallelJoins(
            graph,
            nodesById,
            outgoingTargetsBySource,
            incomingSourcesByTarget,
            diagnostics);
        ValidateBranches(graph, outgoingTargetsBySource, validGeneratedJoinIds, diagnostics);
        ValidateReachability(graph, validOutgoingTargetsBySource, validIncomingSourcesByTarget, diagnostics);
        ValidateCycles(graph, validOutgoingTargetsBySource, diagnostics);

        return diagnostics
            .OrderByDescending(diagnostic => diagnostic.Severity)
            .ThenBy(diagnostic => diagnostic.SectionKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(diagnostic => diagnostic.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ValidateStartCount(QuestGraph graph, List<QuestDiagnostic> diagnostics)
    {
        var count = graph.Nodes.Count(node => node.Kind == QuestGraphNodeKind.Start);
        if (count != 1)
            Add(diagnostics, "graph", "GRAPH_START_COUNT", $"Graph must contain exactly one Start node; found {count}.");
    }

    private static void ValidateCompleteCount(QuestGraph graph, List<QuestDiagnostic> diagnostics)
    {
        var count = graph.Nodes.Count(node => node.Kind == QuestGraphNodeKind.Complete);
        if (count != 1)
            Add(diagnostics, "graph", "GRAPH_COMPLETE_COUNT", $"Graph must contain exactly one Complete node; found {count}.");
    }

    private static void ValidateDuplicateNodeIds(
        Dictionary<string, List<QuestGraphNode>> nodesById,
        List<QuestDiagnostic> diagnostics)
    {
        foreach (var group in nodesById.Where(group => group.Value.Count > 1))
        {
            Add(
                diagnostics,
                SectionForNode(group.Value[0]),
                "GRAPH_DUPLICATE_NODE",
                $"Graph contains {group.Value.Count} nodes with duplicate ID '{NodeIdName(group.Key)}'.");
        }
    }

    private static void ValidateEdges(QuestGraph graph, HashSet<string> nodeIds, List<QuestDiagnostic> diagnostics)
    {
        foreach (var edge in graph.Edges)
        {
            var missingReferences = new List<string>();
            if (!nodeIds.Contains(edge.SourceNodeId))
                missingReferences.Add($"source '{edge.SourceNodeId}'");
            if (!nodeIds.Contains(edge.TargetNodeId))
                missingReferences.Add($"target '{edge.TargetNodeId}'");

            if (missingReferences.Count > 0)
            {
                Add(
                    diagnostics,
                    "graph",
                    "GRAPH_DISCONNECTED_EDGE",
                    $"Edge '{EdgeName(edge)}' references missing {string.Join(" and ", missingReferences)} node.");
            }
        }
    }

    private static void ValidateBranches(
        QuestGraph graph,
        Dictionary<string, List<string>> outgoingTargetsBySource,
        HashSet<string> validGeneratedJoinIds,
        List<QuestDiagnostic> diagnostics)
    {
        foreach (var node in graph.Nodes)
        {
            if (!outgoingTargetsBySource.TryGetValue(node.Id, out var outgoingTargets) || outgoingTargets.Count <= 1)
                continue;
            if (node.IsParallelStage || validGeneratedJoinIds.Contains(node.Id))
                continue;

            Add(
                diagnostics,
                SectionForNode(node),
                "GRAPH_UNSUPPORTED_BRANCH",
                $"Node '{NodeName(node)}' has {outgoingTargets.Count} outgoing edges; arbitrary branching is not supported.");
        }
    }

    private static void ValidateReachability(
        QuestGraph graph,
        Dictionary<string, List<string>> validOutgoingTargetsBySource,
        Dictionary<string, List<string>> validIncomingSourcesByTarget,
        List<QuestDiagnostic> diagnostics)
    {
        HashSet<string>? reachableFromStart = null;
        var startNodes = graph.Nodes.Where(node => node.Kind == QuestGraphNodeKind.Start).ToList();
        if (startNodes.Count == 1)
        {
            reachableFromStart = Traverse(startNodes[0].Id, validOutgoingTargetsBySource);
            foreach (var node in UniqueNodes(graph))
            {
                if (node.Kind == QuestGraphNodeKind.Start || reachableFromStart.Contains(node.Id))
                    continue;

                Add(
                    diagnostics,
                    SectionForNode(node),
                    "GRAPH_UNREACHABLE_NODE",
                    $"Node '{NodeName(node)}' is not reachable from Start.");
            }
        }

        var completeNodes = graph.Nodes.Where(node => node.Kind == QuestGraphNodeKind.Complete).ToList();
        if (completeNodes.Count != 1)
            return;

        var canReachComplete = Traverse(completeNodes[0].Id, validIncomingSourcesByTarget);
        foreach (var node in UniqueNodes(graph))
        {
            if (node.Kind == QuestGraphNodeKind.Complete)
                continue;
            if (reachableFromStart is not null && !reachableFromStart.Contains(node.Id))
                continue;
            if (canReachComplete.Contains(node.Id))
                continue;

            Add(
                diagnostics,
                SectionForNode(node),
                "GRAPH_INCOMPLETE_PATH",
                $"Node '{NodeName(node)}' cannot reach Complete.");
        }
    }

    private static void ValidateCycles(
        QuestGraph graph,
        Dictionary<string, List<string>> validOutgoingTargetsBySource,
        List<QuestDiagnostic> diagnostics)
    {
        var states = new Dictionary<string, VisitState>(StringComparer.Ordinal);
        foreach (var node in UniqueNodes(graph))
        {
            if (states.ContainsKey(node.Id))
                continue;

            if (TryFindCycle(node.Id, validOutgoingTargetsBySource, states, out var cycleNodeId))
            {
                Add(
                    diagnostics,
                    "graph",
                    "GRAPH_CYCLE",
                    $"Graph contains a directed cycle involving node '{NodeIdName(cycleNodeId)}'.");
                return;
            }
        }
    }

    private static HashSet<string> ValidateParallelJoins(
        QuestGraph graph,
        Dictionary<string, List<QuestGraphNode>> nodesById,
        Dictionary<string, List<string>> outgoingTargetsBySource,
        Dictionary<string, List<string>> incomingSourcesByTarget,
        List<QuestDiagnostic> diagnostics)
    {
        var validJoinIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stage in graph.Nodes.Where(node => RequiresGeneratedJoin(node, outgoingTargetsBySource)))
        {
            var joinId = $"{stage.Id}-join";
            if (!nodesById.TryGetValue(joinId, out var joinNodes) || joinNodes.Count == 0)
            {
                Add(
                    diagnostics,
                    SectionForNode(stage),
                    "GRAPH_PARALLEL_JOIN",
                    $"Parallel stage node '{NodeName(stage)}' is missing generated join node '{joinId}'.");
                continue;
            }

            if (joinNodes.Count != 1)
            {
                Add(
                    diagnostics,
                    SectionForNode(stage),
                    "GRAPH_PARALLEL_JOIN",
                    $"Parallel stage node '{NodeName(stage)}' must have exactly one generated join node '{joinId}'.");
                continue;
            }

            var join = joinNodes[0];
            var problems = ValidateJoinShape(stage, join, outgoingTargetsBySource, incomingSourcesByTarget);
            var joinOutgoingCount = outgoingTargetsBySource.TryGetValue(join.Id, out var joinOutgoingTargets)
                ? joinOutgoingTargets.Count
                : 0;

            if (problems.Count > 0)
            {
                Add(
                    diagnostics,
                    SectionForNode(stage),
                    "GRAPH_PARALLEL_JOIN",
                    $"Generated join node '{NodeName(join)}' is malformed: {string.Join("; ", problems)}.");
                continue;
            }

            if (joinOutgoingCount <= 1)
                validJoinIds.Add(join.Id);
        }

        return validJoinIds;
    }

    private static List<string> ValidateJoinShape(
        QuestGraphNode stage,
        QuestGraphNode join,
        Dictionary<string, List<string>> outgoingTargetsBySource,
        Dictionary<string, List<string>> incomingSourcesByTarget)
    {
        var problems = new List<string>();
        if (join.Kind != QuestGraphNodeKind.Stage)
            problems.Add("kind must be Stage");
        if (join.StageNumber != stage.StageNumber)
            problems.Add("StageNumber must match the parallel stage");
        if (join.StageIndex != stage.StageIndex)
            problems.Add("StageIndex must match the parallel stage");

        var expectedFanInSources = outgoingTargetsBySource[stage.Id].ToHashSet(StringComparer.Ordinal);
        var actualFanInSources = incomingSourcesByTarget.TryGetValue(join.Id, out var incomingSources)
            ? incomingSources.ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        if (!expectedFanInSources.SetEquals(actualFanInSources))
            problems.Add("fan-in sources must match the parallel stage fan-out targets");

        return problems;
    }

    private static bool RequiresGeneratedJoin(QuestGraphNode node, Dictionary<string, List<string>> outgoingTargetsBySource)
    {
        return node.IsParallelStage
            && outgoingTargetsBySource.TryGetValue(node.Id, out var outgoingTargets)
            && outgoingTargets.Count > 1;
    }

    private static IEnumerable<QuestGraphNode> UniqueNodes(QuestGraph graph)
    {
        return graph.Nodes
            .GroupBy(node => node.Id, StringComparer.Ordinal)
            .Select(group => group.First());
    }

    private static HashSet<string> Traverse(string startNodeId, Dictionary<string, List<string>> adjacency)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(startNodeId);

        while (pending.Count > 0)
        {
            var nodeId = pending.Pop();
            if (!visited.Add(nodeId))
                continue;

            if (!adjacency.TryGetValue(nodeId, out var nextNodeIds))
                continue;

            foreach (var nextNodeId in nextNodeIds)
                pending.Push(nextNodeId);
        }

        return visited;
    }

    private static bool TryFindCycle(
        string nodeId,
        Dictionary<string, List<string>> adjacency,
        Dictionary<string, VisitState> states,
        out string cycleNodeId)
    {
        if (states.TryGetValue(nodeId, out var state))
        {
            cycleNodeId = nodeId;
            return state == VisitState.Visiting;
        }

        states[nodeId] = VisitState.Visiting;
        if (adjacency.TryGetValue(nodeId, out var nextNodeIds))
        {
            foreach (var nextNodeId in nextNodeIds)
            {
                if (TryFindCycle(nextNodeId, adjacency, states, out cycleNodeId))
                    return true;
            }
        }

        states[nodeId] = VisitState.Visited;
        cycleNodeId = "";
        return false;
    }

    private static string SectionForNode(QuestGraphNode node)
    {
        return string.IsNullOrWhiteSpace(node.Id) ? "graph" : $"node:{node.Id}";
    }

    private static string NodeName(QuestGraphNode node)
    {
        return string.IsNullOrWhiteSpace(node.Id) ? "(blank)" : node.Id;
    }

    private static string NodeIdName(string nodeId)
    {
        return string.IsNullOrWhiteSpace(nodeId) ? "(blank)" : nodeId;
    }

    private static string EdgeName(QuestGraphEdge edge)
    {
        if (!string.IsNullOrWhiteSpace(edge.Id))
            return edge.Id;

        return $"{edge.SourceNodeId}->{edge.TargetNodeId}";
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

    private enum VisitState
    {
        Visiting,
        Visited
    }
}
