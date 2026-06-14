using System.Text.Json;
using QuestParser.Core;

namespace QuestParser.Tests;

public sealed class QuestVisualEditorStateTests
{
    [Fact]
    public void QuestSpecSerializesVisualEditorState()
    {
        var spec = new QuestSpec
        {
            Quest = new QuestMetadata { Name = "Graph Quest", Zone = "Antonica" },
            VisualEditor = new QuestVisualEditorState
            {
                Viewport = new QuestGraphViewport { X = 12, Y = 34, Zoom = 1.25 },
                Nodes =
                [
                    new QuestGraphNodeLayout
                    {
                        Id = "stage-1-step-1",
                        Kind = QuestGraphNodeKind.Step,
                        StageNumber = 1,
                        StepNumber = 1,
                        X = 320,
                        Y = 180,
                        Width = 260,
                        Height = 72,
                        ReviewStatus = QuestVisualReviewStatus.NeedsReview
                    }
                ]
            }
        };

        var json = JsonSerializer.Serialize(spec, QuestSpecJsonContext.Default.QuestSpec);

        Assert.Contains("\"visualEditor\"", json);
        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.Contains("\"kind\": \"Step\"", json);
        Assert.Contains("\"reviewStatus\": \"NeedsReview\"", json);
    }

    [Fact]
    public void QuestSpecDeserializesVisualEditorState()
    {
        const string json = """
        {
          "schemaVersion": "1.0",
          "generationMode": "LegacySpawnStub",
          "quest": { "name": "Graph Quest", "zone": "Antonica" },
          "output": {},
          "provenance": {},
          "questGivers": [],
          "questId": { "kind": "quest", "query": "", "status": "Missing", "source": "Unresolved" },
          "giver": { "kind": "npc", "query": "", "status": "Missing", "source": "Unresolved" },
          "stages": [],
          "rewards": {},
          "todos": [],
          "generation": {},
          "visualEditor": {
            "schemaVersion": 1,
            "layoutVersion": 1,
            "viewport": { "x": 12, "y": 34, "zoom": 1.25 },
            "nodes": [
              {
                "id": "stage-1-step-1",
                "kind": "Step",
                "stageNumber": 1,
                "stepNumber": 1,
                "x": 320,
                "y": 180,
                "width": 260,
                "height": 72,
                "collapsed": false,
                "reviewStatus": "Reviewed"
              }
            ]
          }
        }
        """;

        var spec = JsonSerializer.Deserialize(json, QuestSpecJsonContext.Default.QuestSpec);

        Assert.NotNull(spec);
        Assert.NotNull(spec.VisualEditor);
        Assert.Equal(12, spec.VisualEditor.Viewport.X);
        Assert.Equal(1.25, spec.VisualEditor.Viewport.Zoom);
        Assert.Single(spec.VisualEditor.Nodes);
        Assert.Equal(QuestGraphNodeKind.Step, spec.VisualEditor.Nodes[0].Kind);
        Assert.Equal(QuestVisualReviewStatus.Reviewed, spec.VisualEditor.Nodes[0].ReviewStatus);
    }
}
