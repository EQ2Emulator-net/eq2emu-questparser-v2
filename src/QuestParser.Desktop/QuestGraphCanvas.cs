using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using QuestParser.Core;

namespace QuestParser.Desktop;

internal sealed class QuestGraphCanvas : Control
{
    private const double GridSpacing = 24;
    private const double DefaultNodeWidth = 260;
    private const double DefaultNodeHeight = 72;
    private const double DefaultStageHeight = 54;
    private const double DefaultTerminalSize = 48;
    private const double CornerRadius = 6;

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(248, 250, 252));
    private static readonly IBrush GridBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240));
    private static readonly IBrush NodeFillBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255));
    private static readonly IBrush StageFillBrush = new SolidColorBrush(Color.FromRgb(224, 242, 254));
    private static readonly IBrush TerminalFillBrush = new SolidColorBrush(Color.FromRgb(254, 243, 199));
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.FromRgb(15, 23, 42));
    private static readonly IBrush MutedTextBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105));
    private static readonly IBrush EdgeBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139));
    private static readonly IBrush EdgeLabelBackgroundBrush = new SolidColorBrush(Color.FromRgb(248, 250, 252));

    private static readonly Pen GridPen = new(GridBrush, 0.8);
    private static readonly Pen EdgePen = new(EdgeBrush, 1.4);
    private static readonly Pen NodeBorderPen = new(new SolidColorBrush(Color.FromRgb(203, 213, 225)), 1);
    private static readonly Pen SelectedPen = new(new SolidColorBrush(Color.FromRgb(37, 99, 235)), 2.2);
    private static readonly Pen ConnectionSourcePen = new(new SolidColorBrush(Color.FromRgb(22, 163, 74)), 2.4);
    private static readonly Typeface TitleTypeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface BodyTypeface = new(FontFamily.Default);

    private QuestGraph _graph = new();
    private string _selectedNodeId = "";
    private string _connectionSourceNodeId = "";
    private double _zoom = 1;
    private Vector _graphOffset;
    private QuestGraphNode? _draggedNode;
    private IPointer? _capturedPointer;
    private Vector _dragOffset;
    private bool _nodeMoveStarted;

    public QuestGraphCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public QuestGraph Graph
    {
        get => _graph;
        set
        {
            _graph = value ?? new QuestGraph();
            InvalidateVisual();
        }
    }

    public string SelectedNodeId
    {
        get => _selectedNodeId;
        set
        {
            _selectedNodeId = value ?? "";
            InvalidateVisual();
        }
    }

    public string ConnectionSourceNodeId
    {
        get => _connectionSourceNodeId;
        set
        {
            _connectionSourceNodeId = value ?? "";
            InvalidateVisual();
        }
    }

    public double Zoom
    {
        get => _zoom;
        set
        {
            var zoom = Math.Clamp(value, 0.35, 2.5);
            if (Math.Abs(_zoom - zoom) < 0.001)
                return;

            _zoom = zoom;
            InvalidateVisual();
        }
    }

    public Vector GraphOffset
    {
        get => _graphOffset;
        set
        {
            if (_graphOffset == value)
                return;

            _graphOffset = value;
            InvalidateVisual();
        }
    }

    public event Action<string>? NodeSelected;
    public event Action<string>? NodeMoveStarted;
    public event Action<string, double, double>? NodeMoved;

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = new Rect(Bounds.Size);
        context.DrawRectangle(BackgroundBrush, null, bounds);

        using (context.PushTransform(Matrix.CreateTranslation(GraphOffset.X, GraphOffset.Y) * Matrix.CreateScale(Zoom, Zoom)))
        {
            var graphBounds = new Rect(
                -GraphOffset.X,
                -GraphOffset.Y,
                bounds.Width / Zoom,
                bounds.Height / Zoom);
            DrawGrid(context, graphBounds);
            DrawEdges(context);
            DrawNodes(context);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var pointerPoint = e.GetCurrentPoint(this);
        if (!pointerPoint.Properties.IsLeftButtonPressed)
            return;

        var graphPosition = ToGraphPoint(pointerPoint.Position);
        var node = HitTestNode(graphPosition);
        if (node is null)
            return;

        var nodeRect = GetNodeRect(node);
        _draggedNode = node;
        _capturedPointer = e.Pointer;
        _dragOffset = graphPosition - nodeRect.Position;
        _nodeMoveStarted = false;

        var nodeId = CleanText(node.Id);
        SelectedNodeId = nodeId;
        NodeSelected?.Invoke(nodeId);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_draggedNode is null || _capturedPointer != e.Pointer)
            return;

        var pointerPoint = e.GetCurrentPoint(this);
        if (!pointerPoint.Properties.IsLeftButtonPressed)
        {
            ClearDrag();
            return;
        }

        if (!_nodeMoveStarted)
        {
            _nodeMoveStarted = true;
            NodeMoveStarted?.Invoke(CleanText(_draggedNode.Id));
        }

        var position = ToGraphPoint(pointerPoint.Position) - _dragOffset;
        var layout = GetLayout(_draggedNode);
        layout.X = position.X;
        layout.Y = position.Y;

        NodeMoved?.Invoke(CleanText(_draggedNode.Id), position.X, position.Y);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_draggedNode is null || _capturedPointer != e.Pointer)
            return;

        ClearDrag();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _draggedNode = null;
        _capturedPointer = null;
        _nodeMoveStarted = false;
    }

    private void DrawGrid(DrawingContext context, Rect bounds)
    {
        var startX = Math.Floor(bounds.X / GridSpacing) * GridSpacing + 0.5;
        var startY = Math.Floor(bounds.Y / GridSpacing) * GridSpacing + 0.5;

        for (var x = startX; x <= bounds.Right; x += GridSpacing)
            context.DrawLine(GridPen, new Point(x, bounds.Y), new Point(x, bounds.Bottom));

        for (var y = startY; y <= bounds.Bottom; y += GridSpacing)
            context.DrawLine(GridPen, new Point(bounds.X, y), new Point(bounds.Right, y));
    }

    private void DrawEdges(DrawingContext context)
    {
        var nodesById = new Dictionary<string, QuestGraphNode>(StringComparer.Ordinal);
        foreach (var node in EnumerateNodes())
        {
            var nodeId = node.Id;
            if (!string.IsNullOrWhiteSpace(nodeId))
                nodesById[nodeId] = node;
        }

        foreach (var edge in EnumerateEdges())
        {
            var sourceNodeId = edge.SourceNodeId;
            var targetNodeId = edge.TargetNodeId;
            if (string.IsNullOrWhiteSpace(sourceNodeId)
                || string.IsNullOrWhiteSpace(targetNodeId)
                || !nodesById.TryGetValue(sourceNodeId, out var source)
                || !nodesById.TryGetValue(targetNodeId, out var target))
            {
                continue;
            }

            var sourceRect = GetNodeRect(source);
            var targetRect = GetNodeRect(target);
            var start = GetBoundaryPoint(sourceRect, GetCenter(targetRect));
            var end = GetBoundaryPoint(targetRect, GetCenter(sourceRect));

            context.DrawLine(EdgePen, start, end);
            DrawArrowHead(context, start, end);
            DrawEdgeLabel(context, edge.Label, start, end);
        }
    }

    private void DrawNodes(DrawingContext context)
    {
        foreach (var node in EnumerateNodes())
            DrawNode(context, node);
    }

    private void DrawNode(DrawingContext context, QuestGraphNode node)
    {
        var rect = GetNodeRect(node);
        var fill = GetNodeFill(node.Kind);

        context.DrawRectangle(fill, NodeBorderPen, rect, CornerRadius, CornerRadius);

        if (string.Equals(node.Id, SelectedNodeId, StringComparison.Ordinal))
        {
            var selectedRect = rect.Inflate(2);
            context.DrawRectangle(null, SelectedPen, selectedRect, CornerRadius + 2, CornerRadius + 2);
        }

        if (string.Equals(node.Id, ConnectionSourceNodeId, StringComparison.Ordinal))
        {
            var sourceRect = rect.Inflate(5);
            context.DrawRectangle(null, ConnectionSourcePen, sourceRect, CornerRadius + 4, CornerRadius + 4);
        }

        using (context.PushClip(rect))
            DrawNodeText(context, node, rect);
    }

    private static void DrawNodeText(DrawingContext context, QuestGraphNode node, Rect rect)
    {
        var compact = rect.Width < 90 || rect.Height < 60;
        var horizontalPadding = compact ? 6 : 12;
        var verticalPadding = compact ? 6 : 8;
        var textX = rect.X + horizontalPadding;
        var textY = rect.Y + verticalPadding;
        var textWidth = Math.Max(0, rect.Width - horizontalPadding * 2);
        var titleHeight = compact ? 16 : 20;
        var titleSize = compact ? 10 : 13;
        var title = CleanText(node.Title);
        var fallbackTitle = CleanText(node.Id);

        DrawTrimmedText(
            context,
            string.IsNullOrWhiteSpace(title) ? fallbackTitle : title,
            new Point(textX, textY),
            textWidth,
            titleHeight,
            titleSize,
            TextBrush,
            TitleTypeface);

        var subtitle = CleanText(node.Subtitle);
        var subtitleTop = textY + titleHeight + 2;
        var subtitleHeight = rect.Bottom - verticalPadding - subtitleTop;
        if (!string.IsNullOrWhiteSpace(subtitle) && subtitleHeight >= 12)
        {
            DrawTrimmedText(
                context,
                subtitle,
                new Point(textX, subtitleTop),
                textWidth,
                subtitleHeight,
                compact ? 9 : 11,
                MutedTextBrush,
                BodyTypeface);
        }
    }

    private static void DrawEdgeLabel(DrawingContext context, string? label, Point start, Point end)
    {
        label = CleanText(label);
        if (string.IsNullOrWhiteSpace(label))
            return;

        const double maxWidth = 180;
        var layout = CreateTextLayout(label, maxWidth, 18, 10.5, MutedTextBrush, BodyTypeface);
        var midpoint = new Point((start.X + end.X) / 2, (start.Y + end.Y) / 2);
        var labelRect = new Rect(
            midpoint.X - layout.Width / 2 - 4,
            midpoint.Y - layout.Height / 2 - 2,
            layout.Width + 8,
            layout.Height + 4);

        context.DrawRectangle(EdgeLabelBackgroundBrush, null, labelRect, 4, 4);
        layout.Draw(context, new Point(labelRect.X + 4, labelRect.Y + 2));
    }

    private static void DrawTrimmedText(
        DrawingContext context,
        string? text,
        Point origin,
        double maxWidth,
        double maxHeight,
        double fontSize,
        IBrush brush,
        Typeface typeface)
    {
        text = CleanText(text);
        if (string.IsNullOrWhiteSpace(text) || maxWidth <= 0 || maxHeight <= 0)
            return;

        var layout = CreateTextLayout(text, maxWidth, maxHeight, fontSize, brush, typeface);
        layout.Draw(context, origin);
    }

    private static TextLayout CreateTextLayout(
        string text,
        double maxWidth,
        double maxHeight,
        double fontSize,
        IBrush brush,
        Typeface typeface)
    {
        return new TextLayout(
            text: text,
            typeface: typeface,
            fontSize: fontSize,
            foreground: brush,
            textTrimming: TextTrimming.CharacterEllipsis,
            maxWidth: maxWidth,
            maxHeight: maxHeight,
            maxLines: 1);
    }

    private static void DrawArrowHead(DrawingContext context, Point start, Point end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 8)
            return;

        const double arrowLength = 9;
        const double arrowAngle = Math.PI / 7;
        var angle = Math.Atan2(dy, dx);
        var left = new Point(
            end.X - arrowLength * Math.Cos(angle - arrowAngle),
            end.Y - arrowLength * Math.Sin(angle - arrowAngle));
        var right = new Point(
            end.X - arrowLength * Math.Cos(angle + arrowAngle),
            end.Y - arrowLength * Math.Sin(angle + arrowAngle));

        context.DrawLine(EdgePen, end, left);
        context.DrawLine(EdgePen, end, right);
    }

    private QuestGraphNode? HitTestNode(Point position)
    {
        var nodes = Graph.Nodes;
        if (nodes is null)
            return null;

        for (var index = nodes.Count - 1; index >= 0; index--)
        {
            var node = nodes[index];
            if (node is null)
                continue;

            if (GetNodeRect(node).Contains(position))
                return node;
        }

        return null;
    }

    private void ClearDrag()
    {
        _capturedPointer?.Capture(null);
        _draggedNode = null;
        _capturedPointer = null;
        _nodeMoveStarted = false;
    }

    private Point ToGraphPoint(Point position)
    {
        return new Point(position.X / Zoom - GraphOffset.X, position.Y / Zoom - GraphOffset.Y);
    }

    private static Rect GetNodeRect(QuestGraphNode node)
    {
        var layout = GetLayout(node);
        var width = layout.Width > 0 ? layout.Width : DefaultWidthFor(node.Kind);
        var height = layout.Height > 0 ? layout.Height : DefaultHeightFor(node.Kind);
        return new Rect(layout.X, layout.Y, width, height);
    }

    private static QuestGraphNodeLayout GetLayout(QuestGraphNode node)
    {
        return node.Layout ??= new QuestGraphNodeLayout
        {
            Id = CleanText(node.Id),
            Kind = node.Kind,
            Width = DefaultWidthFor(node.Kind),
            Height = DefaultHeightFor(node.Kind)
        };
    }

    private IEnumerable<QuestGraphNode> EnumerateNodes()
    {
        var nodes = Graph.Nodes;
        if (nodes is null)
            yield break;

        foreach (var node in nodes)
        {
            if (node is not null)
                yield return node;
        }
    }

    private IEnumerable<QuestGraphEdge> EnumerateEdges()
    {
        var edges = Graph.Edges;
        if (edges is null)
            yield break;

        foreach (var edge in edges)
        {
            if (edge is not null)
                yield return edge;
        }
    }

    private static IBrush GetNodeFill(QuestGraphNodeKind kind)
    {
        return kind switch
        {
            QuestGraphNodeKind.Start or QuestGraphNodeKind.Complete => TerminalFillBrush,
            QuestGraphNodeKind.Stage => StageFillBrush,
            _ => NodeFillBrush
        };
    }

    private static double DefaultWidthFor(QuestGraphNodeKind kind)
    {
        return kind is QuestGraphNodeKind.Start or QuestGraphNodeKind.Complete
            ? DefaultTerminalSize
            : DefaultNodeWidth;
    }

    private static double DefaultHeightFor(QuestGraphNodeKind kind)
    {
        return kind switch
        {
            QuestGraphNodeKind.Start or QuestGraphNodeKind.Complete => DefaultTerminalSize,
            QuestGraphNodeKind.Stage => DefaultStageHeight,
            _ => DefaultNodeHeight
        };
    }

    private static Point GetCenter(Rect rect)
    {
        return new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    }

    private static Point GetBoundaryPoint(Rect rect, Point target)
    {
        var center = GetCenter(rect);
        var dx = target.X - center.X;
        var dy = target.Y - center.Y;
        if (Math.Abs(dx) < double.Epsilon && Math.Abs(dy) < double.Epsilon)
            return center;

        var halfWidth = rect.Width / 2;
        var halfHeight = rect.Height / 2;
        var scaleX = Math.Abs(dx) < double.Epsilon ? double.PositiveInfinity : halfWidth / Math.Abs(dx);
        var scaleY = Math.Abs(dy) < double.Epsilon ? double.PositiveInfinity : halfHeight / Math.Abs(dy);
        var scale = Math.Min(scaleX, scaleY);

        return new Point(center.X + dx * scale, center.Y + dy * scale);
    }

    private static string CleanText(string? text)
    {
        if (text is null)
            return "";

        return text
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Trim();
    }
}
