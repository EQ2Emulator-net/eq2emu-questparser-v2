using QuestParser.Core;

namespace QuestParser.Tests;

public sealed class QuestGraphLinearizerTests
{
    [Fact]
    public void MoveStageRenumbersStagesAndSteps()
    {
        var spec = BuildSpec();
        new QuestGraphLinearizer().MoveStage(spec, fromStageIndex: 1, toStageIndex: 0);

        Assert.Equal("Second", spec.Stages[0].Description);
        Assert.Equal(1, spec.Stages[0].Number);
        Assert.Equal(1, spec.Stages[0].Steps[0].Number);
        Assert.Equal("First", spec.Stages[1].Description);
        Assert.Equal(2, spec.Stages[1].Number);
        Assert.Equal(2, spec.Stages[1].Steps[0].Number);
    }

    [Fact]
    public void MoveStepBetweenStagesRenumbersSteps()
    {
        var spec = BuildSpec();
        new QuestGraphLinearizer().MoveStep(spec, fromStageIndex: 0, fromStepIndex: 0, toStageIndex: 1, toStepIndex: 1);

        Assert.Empty(spec.Stages[0].Steps);
        Assert.Equal(2, spec.Stages[1].Steps.Count);
        Assert.Equal(1, spec.Stages[1].Steps[0].Number);
        Assert.Equal("Chat second", spec.Stages[1].Steps[0].Description);
        Assert.Equal(2, spec.Stages[1].Steps[1].Number);
        Assert.Equal("Kill first", spec.Stages[1].Steps[1].Description);
    }

    [Fact]
    public void MoveStepWithinSameStageMovingForwardUsesFinalInsertionIndexAfterRemoval()
    {
        var spec = BuildSingleStageSpec();
        new QuestGraphLinearizer().MoveStep(spec, fromStageIndex: 0, fromStepIndex: 0, toStageIndex: 0, toStepIndex: 2);

        AssertStepOrder(spec, "Step B", "Step C", "Step A", "Step D");
    }

    [Fact]
    public void MoveStepWithinSameStageMovingBackwardUsesFinalInsertionIndexAfterRemoval()
    {
        var spec = BuildSingleStageSpec();
        new QuestGraphLinearizer().MoveStep(spec, fromStageIndex: 0, fromStepIndex: 3, toStageIndex: 0, toStepIndex: 1);

        AssertStepOrder(spec, "Step A", "Step D", "Step B", "Step C");
    }

    [Fact]
    public void MoveStepWithinSameStageNoOpRestoresSameOrderAtFinalInsertionIndexAfterRemoval()
    {
        var spec = BuildSingleStageSpec();
        new QuestGraphLinearizer().MoveStep(spec, fromStageIndex: 0, fromStepIndex: 1, toStageIndex: 0, toStepIndex: 1);

        AssertStepOrder(spec, "Step A", "Step B", "Step C", "Step D");
    }

    [Fact]
    public void MoveStepWithinSameStageAppendClampsToFinalInsertionBoundsAfterRemoval()
    {
        var spec = BuildSingleStageSpec();
        new QuestGraphLinearizer().MoveStep(spec, fromStageIndex: 0, fromStepIndex: 1, toStageIndex: 0, toStepIndex: 99);

        AssertStepOrder(spec, "Step A", "Step C", "Step D", "Step B");
    }

    [Fact]
    public void AddStepCreatesValidStepWithMatchingReferenceKind()
    {
        var spec = BuildSpec();
        var step = new QuestGraphLinearizer().AddStep(spec, stageIndex: 0, StepType.ZoneLocation);

        Assert.Equal(StepType.ZoneLocation, step.Type);
        Assert.Equal("location", step.Target.Kind);
        Assert.NotNull(step.Location);
        Assert.Equal("zone", step.Location.Zone.Kind);
        Assert.Equal(1, step.QuantityMax);
    }

    [Theory]
    [InlineData(StepType.Generic)]
    [InlineData(StepType.Chat)]
    [InlineData(StepType.Craft)]
    [InlineData(StepType.Harvest)]
    [InlineData(StepType.Kill)]
    [InlineData(StepType.KillByRace)]
    [InlineData(StepType.Location)]
    [InlineData(StepType.ObtainItem)]
    [InlineData(StepType.Spell)]
    [InlineData(StepType.ZoneLocation)]
    public void AddStepCreatesValidStepForEveryStepType(StepType type)
    {
        var spec = BuildSpec();
        var step = new QuestGraphLinearizer().AddStep(spec, stageIndex: 0, type);

        Assert.Equal(type, step.Type);
        Assert.Equal(QuestSpecFactory.KindForStepType(type), step.Target.Kind);
        Assert.Equal(1, step.QuantityMax);
        if (type is StepType.Location or StepType.ZoneLocation)
        {
            Assert.NotNull(step.Location);
            Assert.Equal("zone", step.Location.Zone.Kind);
        }
        else
        {
            Assert.Null(step.Location);
        }
    }

    [Fact]
    public void NormalizeNumbersClearsPersistedVisualNodeMetadata()
    {
        var spec = BuildSpecWithVisualMetadata();

        new QuestGraphLinearizer().NormalizeNumbers(spec);

        Assert.NotNull(spec.VisualEditor);
        Assert.Empty(spec.VisualEditor.Nodes);
    }

    [Fact]
    public void MoveStageClearsPersistedVisualNodeMetadata()
    {
        var spec = BuildSpecWithVisualMetadata();

        new QuestGraphLinearizer().MoveStage(spec, fromStageIndex: 1, toStageIndex: 0);

        Assert.NotNull(spec.VisualEditor);
        Assert.Empty(spec.VisualEditor.Nodes);
    }

    [Theory]
    [InlineData(-1, 0, 0, 0, "fromStageIndex")]
    [InlineData(2, 0, 0, 0, "fromStageIndex")]
    [InlineData(0, -1, 0, 0, "fromStepIndex")]
    [InlineData(0, 1, 0, 0, "fromStepIndex")]
    [InlineData(0, 0, -1, 0, "toStageIndex")]
    [InlineData(0, 0, 2, 0, "toStageIndex")]
    public void MoveStepThrowsClearRangeErrorForInvalidIndexes(
        int fromStageIndex,
        int fromStepIndex,
        int toStageIndex,
        int toStepIndex,
        string expectedParamName)
    {
        var spec = BuildSpec();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new QuestGraphLinearizer().MoveStep(spec, fromStageIndex, fromStepIndex, toStageIndex, toStepIndex));

        Assert.Equal(expectedParamName, exception.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void AddStepThrowsClearRangeErrorForInvalidStageIndex(int stageIndex)
    {
        var spec = BuildSpec();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new QuestGraphLinearizer().AddStep(spec, stageIndex, StepType.Kill));

        Assert.Equal("stageIndex", exception.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void RemoveStepThrowsClearRangeErrorForInvalidStageIndex(int stageIndex)
    {
        var spec = BuildSpec();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new QuestGraphLinearizer().RemoveStep(spec, stageIndex, stepIndex: 0));

        Assert.Equal("stageIndex", exception.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void RemoveStepThrowsClearRangeErrorForInvalidStepIndex(int stepIndex)
    {
        var spec = BuildSpec();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new QuestGraphLinearizer().RemoveStep(spec, stageIndex: 0, stepIndex));

        Assert.Equal("stepIndex", exception.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void SetStageParallelThrowsClearRangeErrorForInvalidStageIndex(int stageIndex)
    {
        var spec = BuildSpec();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new QuestGraphLinearizer().SetStageParallel(spec, stageIndex, isParallel: true));

        Assert.Equal("stageIndex", exception.ParamName);
    }

    private static QuestSpec BuildSpec()
    {
        return new QuestSpec
        {
            Quest = new QuestMetadata { Name = "Graph Quest", Zone = "Antonica" },
            Stages =
            [
                new QuestStageSpec
                {
                    Number = 1,
                    Description = "First",
                    Steps =
                    [
                        new QuestStepSpec
                        {
                            Number = 1,
                            Type = StepType.Kill,
                            Description = "Kill first",
                            CompletedDescription = "Killed first",
                            QuantityMax = 1
                        }
                    ]
                },
                new QuestStageSpec
                {
                    Number = 2,
                    Description = "Second",
                    Steps =
                    [
                        new QuestStepSpec
                        {
                            Number = 2,
                            Type = StepType.Chat,
                            Description = "Chat second",
                            CompletedDescription = "Chatted second",
                            QuantityMax = 1
                        }
                    ]
                }
            ]
        };
    }

    private static QuestSpec BuildSingleStageSpec()
    {
        return new QuestSpec
        {
            Quest = new QuestMetadata { Name = "Graph Quest", Zone = "Antonica" },
            Stages =
            [
                new QuestStageSpec
                {
                    Number = 1,
                    Description = "Only",
                    Steps =
                    [
                        CreateStep(1, "Step A"),
                        CreateStep(2, "Step B"),
                        CreateStep(3, "Step C"),
                        CreateStep(4, "Step D")
                    ]
                }
            ]
        };
    }

    private static QuestSpec BuildSpecWithVisualMetadata()
    {
        var spec = BuildSpec();
        spec.VisualEditor = new QuestVisualEditorState
        {
            Nodes =
            [
                new QuestGraphNodeLayout
                {
                    Id = "stage-1",
                    Kind = QuestGraphNodeKind.Stage,
                    StageNumber = 1,
                    X = 100,
                    Y = 200,
                    Collapsed = true,
                    ReviewStatus = QuestVisualReviewStatus.Reviewed
                },
                new QuestGraphNodeLayout
                {
                    Id = "stage-1-step-1",
                    Kind = QuestGraphNodeKind.Step,
                    StageNumber = 1,
                    StepNumber = 1,
                    X = 300,
                    Y = 400,
                    ReviewStatus = QuestVisualReviewStatus.Modified
                },
                new QuestGraphNodeLayout
                {
                    Id = "stage-1-join",
                    Kind = QuestGraphNodeKind.Complete,
                    StageNumber = 1,
                    X = 500,
                    Y = 600,
                    ReviewStatus = QuestVisualReviewStatus.Reviewed
                },
                new QuestGraphNodeLayout
                {
                    Id = "stage-1-step-1-option-0",
                    Kind = QuestGraphNodeKind.RandomOption,
                    StageNumber = 1,
                    StepNumber = 1,
                    OptionIndex = 0,
                    X = 700,
                    Y = 800,
                    ReviewStatus = QuestVisualReviewStatus.Reviewed
                }
            ]
        };

        return spec;
    }

    private static QuestStepSpec CreateStep(int number, string description)
    {
        return new QuestStepSpec
        {
            Number = number,
            Type = StepType.Kill,
            Description = description,
            CompletedDescription = $"{description} complete",
            QuantityMax = 1
        };
    }

    private static void AssertStepOrder(QuestSpec spec, params string[] descriptions)
    {
        var steps = spec.Stages[0].Steps;
        Assert.Equal(descriptions, steps.Select(step => step.Description));
        Assert.Equal(Enumerable.Range(1, descriptions.Length), steps.Select(step => step.Number));
    }
}
