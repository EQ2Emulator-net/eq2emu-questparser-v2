using System.Text;
using System.Text.Json;
using QuestParser.Core;

namespace QuestParser.Desktop;

internal static class VisualEditorDefinitionBuilder
{
    public static string Build(QuestSpec? spec, QuestGraphNode? selectedNode)
    {
        if (spec is null)
            return "";

        if (selectedNode is null)
            return JsonSerializer.Serialize(spec, QuestSpecJsonContext.Default.QuestSpec);

        if (selectedNode.StageIndex is int stageIndex && selectedNode.StepIndex is int stepIndex)
        {
            return TryGetStep(spec, stageIndex, stepIndex, out var stage, out var step)
                ? BuildStepDefinition(selectedNode, stage, step)
                : BuildNodeDefinition(selectedNode);
        }

        if (selectedNode.StageIndex is int selectedStageIndex
            && TryGetStage(spec, selectedStageIndex, out var selectedStage))
        {
            return BuildStageDefinition(selectedNode, selectedStage);
        }

        return BuildNodeDefinition(selectedNode);
    }

    private static string BuildStepDefinition(QuestGraphNode node, QuestStageSpec stage, QuestStepSpec step)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Node id: {node.Id}");
        builder.AppendLine($"Kind: {node.Kind}");
        builder.AppendLine($"Stage number: {stage.Number}");
        builder.AppendLine($"Step number: {step.Number}");
        builder.AppendLine($"Step type: {step.Type}");
        builder.AppendLine($"Description: {step.Description}");
        builder.AppendLine($"Completed description: {step.CompletedDescription}");
        builder.AppendLine($"Quantity min: {step.QuantityMin}");
        builder.AppendLine($"Quantity max: {step.QuantityMax}");
        builder.AppendLine($"Search text: {step.SearchText}");
        builder.AppendLine($"Target kind: {step.Target.Kind}");
        builder.AppendLine($"Target status: {step.Target.Status}");
        builder.AppendLine($"Target id: {step.Target.Id?.ToString() ?? ""}");
        return builder.ToString();
    }

    private static string BuildStageDefinition(QuestGraphNode node, QuestStageSpec stage)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Node id: {node.Id}");
        builder.AppendLine($"Kind: {node.Kind}");
        builder.AppendLine($"Stage number: {stage.Number}");
        builder.AppendLine($"Parallel: {stage.IsParallel}");
        builder.AppendLine($"Description: {stage.Description}");
        builder.AppendLine($"Completed description: {stage.CompletedDescription}");
        return builder.ToString();
    }

    private static string BuildNodeDefinition(QuestGraphNode node)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Node id: {node.Id}");
        builder.AppendLine($"Kind: {node.Kind}");
        builder.AppendLine($"Title: {node.Title}");
        builder.AppendLine($"Subtitle: {node.Subtitle}");
        return builder.ToString();
    }

    private static bool TryGetStep(
        QuestSpec spec,
        int stageIndex,
        int stepIndex,
        out QuestStageSpec stage,
        out QuestStepSpec step)
    {
        stage = null!;
        step = null!;

        if (!TryGetStage(spec, stageIndex, out stage))
            return false;

        if (stepIndex < 0 || stepIndex >= stage.Steps.Count)
            return false;

        step = stage.Steps[stepIndex];
        return true;
    }

    private static bool TryGetStage(QuestSpec spec, int stageIndex, out QuestStageSpec stage)
    {
        stage = null!;
        if (stageIndex < 0 || stageIndex >= spec.Stages.Count)
            return false;

        stage = spec.Stages[stageIndex];
        return true;
    }
}
