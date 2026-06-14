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
