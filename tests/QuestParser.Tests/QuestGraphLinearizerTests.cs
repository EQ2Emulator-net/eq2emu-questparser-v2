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
}
