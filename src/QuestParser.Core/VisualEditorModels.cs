using System.Text.Json.Serialization;

namespace QuestParser.Core;

[JsonConverter(typeof(JsonStringEnumConverter<QuestGraphNodeKind>))]
public enum QuestGraphNodeKind
{
    Start,
    Complete,
    Stage,
    Step,
    RandomOptions,
    RandomOption,
    Comment
}

[JsonConverter(typeof(JsonStringEnumConverter<QuestVisualReviewStatus>))]
public enum QuestVisualReviewStatus
{
    Imported,
    NeedsReview,
    Reviewed,
    Modified,
    Invalid
}

public sealed class QuestVisualEditorState
{
    public int SchemaVersion { get; set; } = 1;
    public int LayoutVersion { get; set; } = 1;
    public QuestGraphViewport Viewport { get; set; } = new();
    public List<QuestGraphNodeLayout> Nodes { get; set; } = [];
}

public sealed class QuestGraphViewport
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Zoom { get; set; } = 1;
}

public sealed class QuestGraphNodeLayout
{
    public string Id { get; set; } = "";
    public QuestGraphNodeKind Kind { get; set; }
    public int? StageNumber { get; set; }
    public int? StepNumber { get; set; }
    public int? OptionIndex { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 260;
    public double Height { get; set; } = 72;
    public bool Collapsed { get; set; }
    public QuestVisualReviewStatus ReviewStatus { get; set; } = QuestVisualReviewStatus.NeedsReview;
}
