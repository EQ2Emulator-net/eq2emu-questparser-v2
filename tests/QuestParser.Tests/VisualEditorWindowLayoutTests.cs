using Avalonia;
using QuestParser.Core;
using QuestParser.Desktop;

namespace QuestParser.Tests;

public sealed class VisualEditorWindowLayoutTests
{
    [Fact]
    public void CalculateGraphCanvasViewportOffsetsNegativeGraphCoordinatesIntoScrollableCanvas()
    {
        var graph = new QuestGraph
        {
            Nodes =
            [
                new QuestGraphNode
                {
                    Id = "left",
                    Kind = QuestGraphNodeKind.Step,
                    Layout = new QuestGraphNodeLayout
                    {
                        X = -3330,
                        Y = 60,
                        Width = 260,
                        Height = 72
                    }
                },
                new QuestGraphNode
                {
                    Id = "right",
                    Kind = QuestGraphNodeKind.Step,
                    Layout = new QuestGraphNodeLayout
                    {
                        X = 720,
                        Y = 60,
                        Width = 260,
                        Height = 72
                    }
                }
            ]
        };

        var viewport = VisualEditorWindow.CalculateGraphCanvasViewport(graph, zoom: 1);

        Assert.Equal(3570, viewport.GraphOffset.X);
        Assert.Equal(180, viewport.GraphOffset.Y);
        Assert.True(viewport.Width >= 4550);
        Assert.True(viewport.Height >= 1400);
    }

    [Fact]
    public void CalculateGraphCanvasViewportScalesCanvasExtentForZoom()
    {
        var graph = new QuestGraph
        {
            Nodes =
            [
                new QuestGraphNode
                {
                    Id = "node",
                    Kind = QuestGraphNodeKind.Step,
                    Layout = new QuestGraphNodeLayout
                    {
                        X = -100,
                        Y = 0,
                        Width = 260,
                        Height = 72
                    }
                }
            ]
        };

        var normal = VisualEditorWindow.CalculateGraphCanvasViewport(graph, zoom: 1);
        var zoomed = VisualEditorWindow.CalculateGraphCanvasViewport(graph, zoom: 2);

        Assert.Equal(normal.Width * 2, zoomed.Width);
        Assert.Equal(normal.Height * 2, zoomed.Height);
        Assert.Equal(normal.GraphOffset, zoomed.GraphOffset);
    }
}
