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

        ValidateStartCount(graph, diagnostics);
        ValidateCompleteCount(graph, diagnostics);
        ValidateEdges(graph, nodeIds, diagnostics);
        var validGeneratedJoinIds = ValidateParallelJoins(
            graph,
            nodesById,
            outgoingTargetsBySource,
            incomingSourcesByTarget,
            diagnostics);
        ValidateBranches(graph, outgoingTargetsBySource, validGeneratedJoinIds, diagnostics);

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

    private static string SectionForNode(QuestGraphNode node)
    {
        return string.IsNullOrWhiteSpace(node.Id) ? "graph" : $"node:{node.Id}";
    }

    private static string NodeName(QuestGraphNode node)
    {
        return string.IsNullOrWhiteSpace(node.Id) ? "(blank)" : node.Id;
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
}
