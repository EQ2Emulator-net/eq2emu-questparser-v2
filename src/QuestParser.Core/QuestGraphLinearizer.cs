namespace QuestParser.Core;

public sealed class QuestGraphLinearizer
{
    public void MoveStage(QuestSpec spec, int fromStageIndex, int toStageIndex)
    {
        ValidateStageIndex(spec, fromStageIndex, nameof(fromStageIndex));
        ValidateStageIndex(spec, toStageIndex, nameof(toStageIndex));

        var stage = spec.Stages[fromStageIndex];
        spec.Stages.RemoveAt(fromStageIndex);
        spec.Stages.Insert(toStageIndex, stage);
        NormalizeNumbers(spec);
    }

    public void MoveStep(QuestSpec spec, int fromStageIndex, int fromStepIndex, int toStageIndex, int toStepIndex)
    {
        ValidateStageIndex(spec, fromStageIndex, nameof(fromStageIndex));
        ValidateStageIndex(spec, toStageIndex, nameof(toStageIndex));
        var fromStage = spec.Stages[fromStageIndex];
        var toStage = spec.Stages[toStageIndex];
        ValidateStepIndex(fromStage, fromStepIndex, nameof(fromStepIndex));

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
        ValidateStageIndex(spec, stageIndex, nameof(stageIndex));
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
        ValidateStageIndex(spec, stageIndex, nameof(stageIndex));
        var stage = spec.Stages[stageIndex];
        ValidateStepIndex(stage, stepIndex, nameof(stepIndex));

        stage.Steps.RemoveAt(stepIndex);
        NormalizeNumbers(spec);
    }

    public void RemoveStage(QuestSpec spec, int stageIndex)
    {
        ValidateStageIndex(spec, stageIndex, nameof(stageIndex));

        spec.Stages.RemoveAt(stageIndex);
        NormalizeNumbers(spec);
    }

    public void SetStageParallel(QuestSpec spec, int stageIndex, bool isParallel)
    {
        ValidateStageIndex(spec, stageIndex, nameof(stageIndex));
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

        InvalidateVisualLayout(spec);
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

    private static void ValidateStageIndex(QuestSpec spec, int stageIndex, string paramName)
    {
        if (stageIndex < 0 || stageIndex >= spec.Stages.Count)
            throw new ArgumentOutOfRangeException(paramName);
    }

    private static void ValidateStepIndex(QuestStageSpec stage, int stepIndex, string paramName)
    {
        if (stepIndex < 0 || stepIndex >= stage.Steps.Count)
            throw new ArgumentOutOfRangeException(paramName);
    }

    private static void InvalidateVisualLayout(QuestSpec spec)
    {
        spec.VisualEditor?.Nodes.Clear();
    }
}
