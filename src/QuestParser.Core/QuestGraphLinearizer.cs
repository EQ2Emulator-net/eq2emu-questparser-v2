namespace QuestParser.Core;

public sealed class QuestGraphLinearizer
{
    public void MoveStage(QuestSpec spec, int fromStageIndex, int toStageIndex)
    {
        if (fromStageIndex < 0 || fromStageIndex >= spec.Stages.Count)
            throw new ArgumentOutOfRangeException(nameof(fromStageIndex));
        if (toStageIndex < 0 || toStageIndex >= spec.Stages.Count)
            throw new ArgumentOutOfRangeException(nameof(toStageIndex));

        var stage = spec.Stages[fromStageIndex];
        spec.Stages.RemoveAt(fromStageIndex);
        spec.Stages.Insert(toStageIndex, stage);
        NormalizeNumbers(spec);
    }

    public void MoveStep(QuestSpec spec, int fromStageIndex, int fromStepIndex, int toStageIndex, int toStepIndex)
    {
        var fromStage = spec.Stages[fromStageIndex];
        var toStage = spec.Stages[toStageIndex];
        var step = fromStage.Steps[fromStepIndex];
        fromStage.Steps.RemoveAt(fromStepIndex);

        var boundedIndex = Math.Clamp(toStepIndex, 0, toStage.Steps.Count);
        toStage.Steps.Insert(boundedIndex, step);
        NormalizeNumbers(spec);
    }

    public QuestStageSpec AddStage(QuestSpec spec, bool isParallel)
    {
        var stage = new QuestStageSpec
        {
            Description = isParallel ? "Parallel task group" : "Task group",
            CompletedDescription = isParallel ? "Parallel task group complete" : "Task group complete",
            IsParallel = isParallel
        };
        spec.Stages.Add(stage);
        NormalizeNumbers(spec);
        return stage;
    }

    public QuestStepSpec AddStep(QuestSpec spec, int stageIndex, StepType stepType)
    {
        var stage = spec.Stages[stageIndex];
        var kind = QuestSpecFactory.KindForStepType(stepType);
        var step = new QuestStepSpec
        {
            Type = stepType,
            Description = $"{DisplayName(stepType)} objective",
            CompletedDescription = $"{DisplayName(stepType)} objective complete",
            QuantityMin = 0,
            QuantityMax = 1,
            Percentage = 100,
            SearchText = "",
            Target = ResolvedReference.Missing(kind, "")
        };

        if (stepType is StepType.Location or StepType.ZoneLocation)
        {
            step.Location = new LocationTarget
            {
                Radius = 10,
                Zone = ResolvedReference.Missing("zone", spec.Quest.Zone)
            };
        }

        stage.Steps.Add(step);
        NormalizeNumbers(spec);
        return step;
    }

    public void RemoveStep(QuestSpec spec, int stageIndex, int stepIndex)
    {
        spec.Stages[stageIndex].Steps.RemoveAt(stepIndex);
        NormalizeNumbers(spec);
    }

    public void SetStageParallel(QuestSpec spec, int stageIndex, bool isParallel)
    {
        spec.Stages[stageIndex].IsParallel = isParallel;
        NormalizeNumbers(spec);
    }

    public void NormalizeNumbers(QuestSpec spec)
    {
        var nextStepNumber = 1;
        for (var stageIndex = 0; stageIndex < spec.Stages.Count; stageIndex++)
        {
            var stage = spec.Stages[stageIndex];
            stage.Number = stageIndex + 1;
            foreach (var step in stage.Steps)
                step.Number = nextStepNumber++;
        }
    }

    private static string DisplayName(StepType type)
    {
        return type switch
        {
            StepType.KillByRace => "Kill by race",
            StepType.ObtainItem => "Obtain item",
            StepType.ZoneLocation => "Zone location",
            _ => type.ToString()
        };
    }
}
