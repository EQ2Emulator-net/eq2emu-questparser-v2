namespace QuestParser.Core;

public sealed class QuestGraphValidator
{
    public List<QuestDiagnostic> Validate(QuestGraph graph)
    {
        var diagnostics = new List<QuestDiagnostic>();
        var nodeIds = graph.Nodes
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);
        var generatedJoinIds = graph.Nodes
            .Where(node => node.IsParallelStage)
            .Select(node => $"{node.Id}-join")
            .ToHashSet(StringComparer.Ordinal);

        ValidateStartCount(graph, diagnostics);
        ValidateCompleteCount(graph, diagnostics);
        ValidateEdges(graph, nodeIds, diagnostics);
        ValidateBranches(graph, generatedJoinIds, diagnostics);
        ValidateParallelJoins(graph, nodeIds, diagnostics);

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
        HashSet<string> generatedJoinIds,
        List<QuestDiagnostic> diagnostics)
    {
        var outgoingCounts = graph.Edges
            .GroupBy(edge => edge.SourceNodeId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        foreach (var node in graph.Nodes)
        {
            if (!outgoingCounts.TryGetValue(node.Id, out var outgoingCount) || outgoingCount <= 1)
                continue;
            if (node.IsParallelStage || generatedJoinIds.Contains(node.Id))
                continue;

            Add(
                diagnostics,
                SectionForNode(node),
                "GRAPH_UNSUPPORTED_BRANCH",
                $"Node '{NodeName(node)}' has {outgoingCount} outgoing edges; arbitrary branching is not supported.");
        }
    }

    private static void ValidateParallelJoins(
        QuestGraph graph,
        HashSet<string> nodeIds,
        List<QuestDiagnostic> diagnostics)
    {
        foreach (var stage in graph.Nodes.Where(node => node.IsParallelStage))
        {
            var joinId = $"{stage.Id}-join";
            if (nodeIds.Contains(joinId))
                continue;

            Add(
                diagnostics,
                SectionForNode(stage),
                "GRAPH_PARALLEL_JOIN",
                $"Parallel stage node '{NodeName(stage)}' is missing generated join node '{joinId}'.");
        }
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
