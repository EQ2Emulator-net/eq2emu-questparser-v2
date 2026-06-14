namespace QuestParser.Core;

public sealed class QuestGraphLayoutService
{
    private const double CenterX = 420;
    private const double StageSpacingY = 180;
    private const double StepSpacingX = 300;
    private const double NodeWidth = 260;
    private const double StepHeight = 72;
    private const double StageHeight = 54;
    private const double CircleSize = 48;

    public QuestGraphNodeLayout LayoutFor(QuestSpec spec, QuestGraphNode node, int orderIndex, int siblingIndex = 0, int siblingCount = 1)
    {
        var existing = FindExistingLayout(spec.VisualEditor, node);
        if (existing is not null)
            return existing;

        var y = 60 + orderIndex * StageSpacingY;
        var x = CenterX;
        if (siblingCount > 1)
            x = CenterX + (siblingIndex - (siblingCount - 1) / 2.0) * StepSpacingX;

        var width = node.Kind is QuestGraphNodeKind.Start or QuestGraphNodeKind.Complete ? CircleSize : NodeWidth;
        var height = node.Kind is QuestGraphNodeKind.Stage ? StageHeight : StepHeight;
        if (node.Kind is QuestGraphNodeKind.Start or QuestGraphNodeKind.Complete)
            height = CircleSize;

        return new QuestGraphNodeLayout
        {
            Id = node.Id,
            Kind = node.Kind,
            StageNumber = node.StageNumber,
            StepNumber = node.StepNumber,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            ReviewStatus = QuestVisualReviewStatus.NeedsReview
        };
    }

    public void EnsureVisualState(QuestSpec spec, QuestGraph graph)
    {
        spec.VisualEditor ??= new QuestVisualEditorState();
        var retained = new List<QuestGraphNodeLayout>();
        foreach (var node in graph.Nodes)
            retained.Add(node.Layout);
        spec.VisualEditor.Nodes = retained;
    }

    private static QuestGraphNodeLayout? FindExistingLayout(QuestVisualEditorState? state, QuestGraphNode node)
    {
        if (state is null)
            return null;

        var exact = state.Nodes.FirstOrDefault(layout => string.Equals(layout.Id, node.Id, StringComparison.Ordinal));
        if (exact is not null)
            return exact;

        return state.Nodes.FirstOrDefault(layout =>
            layout.Kind == node.Kind
            && layout.StageNumber == node.StageNumber
            && layout.StepNumber == node.StepNumber);
    }
}
