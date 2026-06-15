using System.Collections.ObjectModel;
using System.Text;
using QuestParser.Core;

namespace QuestParser.Desktop;

internal sealed class VisualEditorViewModel
{
    private readonly QuestWorkflow _workflow;
    private readonly QuestGraphProjector _projector = new();
    private readonly QuestGraphLinearizer _linearizer = new();
    private readonly QuestGraphValidator _graphValidator = new();

    public VisualEditorViewModel(QuestWorkflow workflow, QuestSpec? spec = null)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));

        if (spec is not null)
            LoadSpec(spec);
    }

    public QuestSpec? Spec { get; private set; }
    public QuestGraph Graph { get; private set; } = new();
    public QuestGraphNode? SelectedNode { get; private set; }
    public ObservableCollection<string> Diagnostics { get; } = [];
    public ObservableCollection<string> GenerationLog { get; } = [];
    public string LuaPreview { get; private set; } = "";
    public string SqlPreview { get; private set; } = "";
    public string MissingPreview { get; private set; } = "";
    public string Walkthrough { get; private set; } = "";
    public string Definition { get; private set; } = "";
    public bool IsDirty { get; private set; }

    public void LoadSpec(QuestSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        ClearPreviewState();
        Spec = spec;
        SelectedNode = null;
        RebuildGraph();
        RefreshPreview();
        IsDirty = false;
    }

    public void SelectNode(string nodeId)
    {
        SelectedNode = Graph.Nodes.FirstOrDefault(
            node => string.Equals(node.Id, nodeId, StringComparison.Ordinal));
        Definition = VisualEditorDefinitionBuilder.Build(Spec, SelectedNode);
    }

    public void AddStage(bool isParallel)
    {
        if (Spec is null)
            return;

        _linearizer.AddStage(Spec, isParallel);
        IsDirty = true;
        RebuildGraph();
        RefreshPreview();
    }

    public void AddStep(int stageIndex, StepType stepType)
    {
        if (Spec is null)
            return;

        if (stageIndex < 0 || stageIndex >= Spec.Stages.Count)
        {
            AddGenerationLogEntry($"Cannot add step: stage index {stageIndex} is not valid for {Spec.Stages.Count} stage(s).");
            return;
        }

        _linearizer.AddStep(Spec, stageIndex, stepType);
        IsDirty = true;
        RebuildGraph();
        RefreshPreview();
    }

    public void SetStageParallel(int stageIndex, bool isParallel)
    {
        if (Spec is null)
            return;

        if (stageIndex < 0 || stageIndex >= Spec.Stages.Count)
        {
            AddGenerationLogEntry($"Cannot update stage: stage index {stageIndex} is not valid for {Spec.Stages.Count} stage(s).");
            return;
        }

        _linearizer.SetStageParallel(Spec, stageIndex, isParallel);
        IsDirty = true;
        RebuildGraph();
        RefreshPreview();
    }

    public void MoveStepToStage(int fromStageIndex, int fromStepIndex, int toStageIndex)
    {
        if (Spec is null)
            return;

        _ = TryMoveStep(fromStageIndex, fromStepIndex, toStageIndex, Spec.Stages.ElementAtOrDefault(toStageIndex)?.Steps.Count ?? 0);
    }

    public bool DeleteNode(string nodeId)
    {
        if (Spec is null)
            return false;

        var node = FindNode(nodeId);
        if (node is null)
        {
            AddGenerationLogEntry($"Cannot delete node: '{nodeId}' was not found.");
            return false;
        }

        if (IsStepNode(node))
            return DeleteStepNode(node);

        if (node.Kind == QuestGraphNodeKind.Stage)
            return DeleteStageNode(node);

        AddGenerationLogEntry($"Cannot delete node '{node.Id}': {node.Kind} nodes are generated workflow anchors.");
        return false;
    }

    public bool ConnectNodes(string sourceNodeId, string targetNodeId)
    {
        if (Spec is null)
            return false;

        var source = FindNode(sourceNodeId);
        var target = FindNode(targetNodeId);
        if (source is null || target is null)
        {
            AddGenerationLogEntry("Cannot connect nodes: source or target was not found.");
            return false;
        }

        if (string.Equals(source.Id, target.Id, StringComparison.Ordinal))
        {
            AddGenerationLogEntry("Cannot connect a node to itself.");
            return false;
        }

        if (source.Kind == QuestGraphNodeKind.Stage && target.Kind == QuestGraphNodeKind.Stage)
            return MoveStageAfter(source, target);

        if (IsStepNode(source) && target.Kind == QuestGraphNodeKind.Stage)
            return MoveStepToStageEnd(source, target);

        if (IsStepNode(source) && IsStepNode(target))
            return MoveStepAfterStep(source, target);

        AddGenerationLogEntry($"Cannot connect {source.Kind} to {target.Kind}: this relationship is not represented by generated quest Lua.");
        return false;
    }

    public void MarkDirty()
    {
        if (Spec is not null)
            IsDirty = true;
    }

    public void MoveNode(string nodeId, double x, double y)
    {
        if (Spec is null)
            return;

        var node = Graph.Nodes.FirstOrDefault(
            candidate => string.Equals(candidate.Id, nodeId, StringComparison.Ordinal));
        if (node is null)
            return;

        Spec.VisualEditor ??= new QuestVisualEditorState();
        var layout = Spec.VisualEditor.Nodes.FirstOrDefault(
            candidate => string.Equals(candidate.Id, nodeId, StringComparison.Ordinal));
        if (layout is null)
        {
            layout = new QuestGraphNodeLayout
            {
                Id = node.Id,
                Kind = node.Kind,
                StageNumber = node.StageNumber,
                StepNumber = node.StepNumber,
                X = x,
                Y = y,
                Width = node.Layout.Width,
                Height = node.Layout.Height,
                Collapsed = node.Layout.Collapsed,
                ReviewStatus = node.Layout.ReviewStatus
            };
            Spec.VisualEditor.Nodes.Add(layout);
        }
        else
        {
            layout.X = x;
            layout.Y = y;
        }

        IsDirty = true;
        RebuildGraph();
    }

    public IReadOnlyList<QuestDiagnostic> Validate()
    {
        Diagnostics.Clear();

        if (Spec is null)
        {
            Diagnostics.Add("No diagnostics.");
            return [];
        }

        var diagnostics = _graphValidator.Validate(Graph)
            .Concat(QuestSpecValidator.Validate(Spec, overwrite: true))
            .OrderByDescending(diagnostic => diagnostic.Severity)
            .ThenBy(diagnostic => diagnostic.SectionKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(diagnostic => diagnostic.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (diagnostics.Count == 0)
        {
            Diagnostics.Add("No diagnostics.");
            return diagnostics;
        }

        foreach (var diagnostic in diagnostics)
            Diagnostics.Add(FormatDiagnostic(diagnostic));

        return diagnostics;
    }

    public void RefreshPreview()
    {
        if (Spec is null)
        {
            ClearPreviewState();
            return;
        }

        var diagnostics = Validate();
        RefreshTextState();

        if (diagnostics.Any(diagnostic => diagnostic.Severity == QuestDiagnosticSeverity.Blocker)
            && !string.IsNullOrEmpty(LuaPreview))
        {
            AddGenerationLogEntry("Preview not refreshed because diagnostics contain blockers; stale generated output is still shown.");
            return;
        }

        try
        {
            var preview = _workflow.Preview(Spec);
            LuaPreview = preview.Lua;
            SqlPreview = preview.Sql;
            MissingPreview = preview.MissingReport;
        }
        catch (Exception ex)
        {
            AddGenerationLogEntry("Preview failed: " + ex.Message);
        }
    }

    public async Task SaveSpecAsync(CancellationToken cancellationToken = default)
    {
        if (Spec is null)
            return;

        await QuestWorkflow.WriteSpecAsync(Spec, cancellationToken).ConfigureAwait(false);
        IsDirty = false;
    }

    public async Task<QuestWorkflowResult?> GenerateAsync(bool overwrite, CancellationToken cancellationToken = default)
    {
        if (Spec is null)
            return null;

        var result = await _workflow.GenerateFromSpecAsync(
            Spec,
            overwrite,
            cancellationToken,
            Spec.GenerationMode,
            strictModuleLuaValidation: true).ConfigureAwait(false);

        Spec = result.Spec;
        RebuildGraph();
        LuaPreview = result.Lua;
        SqlPreview = result.Sql;
        MissingPreview = result.MissingReport;
        Walkthrough = BuildWalkthrough(Spec);
        Definition = VisualEditorDefinitionBuilder.Build(Spec, SelectedNode);
        IsDirty = false;

        return result;
    }

    private void ClearPreviewState()
    {
        LuaPreview = "";
        SqlPreview = "";
        MissingPreview = "";
        Walkthrough = "";
        Definition = "";
        Diagnostics.Clear();
        GenerationLog.Clear();
    }

    private void RebuildGraph()
    {
        if (Spec is null)
        {
            Graph = new QuestGraph();
            SelectedNode = null;
            Definition = "";
            return;
        }

        var selectedNodeId = SelectedNode?.Id;
        Graph = _projector.Project(Spec);
        SelectedNode = selectedNodeId is null
            ? null
            : Graph.Nodes.FirstOrDefault(node => string.Equals(node.Id, selectedNodeId, StringComparison.Ordinal));
        Definition = VisualEditorDefinitionBuilder.Build(Spec, SelectedNode);
    }

    private void RefreshTextState()
    {
        if (Spec is null)
        {
            Walkthrough = "";
            Definition = "";
            return;
        }

        Walkthrough = BuildWalkthrough(Spec);
        Definition = VisualEditorDefinitionBuilder.Build(Spec, SelectedNode);
    }

    private void AddGenerationLogEntry(string message)
    {
        if (GenerationLog.Count > 0
            && string.Equals(GenerationLog[^1], message, StringComparison.Ordinal))
        {
            return;
        }

        GenerationLog.Add(message);
    }

    private bool DeleteStepNode(QuestGraphNode node)
    {
        if (Spec is null
            || node.StageIndex is not int stageIndex
            || node.StepIndex is not int stepIndex
            || !HasStep(stageIndex, stepIndex))
        {
            AddGenerationLogEntry($"Cannot delete step node '{node.Id}': the node is stale.");
            return false;
        }

        _linearizer.RemoveStep(Spec, stageIndex, stepIndex);
        CompleteStructuralEdit(selectNode: null);
        return true;
    }

    private bool DeleteStageNode(QuestGraphNode node)
    {
        if (Spec is null
            || node.StageIndex is not int stageIndex
            || !HasStage(stageIndex))
        {
            AddGenerationLogEntry($"Cannot delete stage node '{node.Id}': the node is stale.");
            return false;
        }

        _linearizer.RemoveStage(Spec, stageIndex);
        CompleteStructuralEdit(selectNode: null);
        return true;
    }

    private bool MoveStageAfter(QuestGraphNode source, QuestGraphNode target)
    {
        if (Spec is null
            || source.StageIndex is not int sourceStageIndex
            || target.StageIndex is not int targetStageIndex
            || !HasStage(sourceStageIndex)
            || !HasStage(targetStageIndex))
        {
            AddGenerationLogEntry("Cannot move stage: source or target stage is stale.");
            return false;
        }

        if (sourceStageIndex == targetStageIndex)
            return false;

        var movedStage = Spec.Stages[sourceStageIndex];
        var insertionIndex = sourceStageIndex < targetStageIndex
            ? targetStageIndex
            : targetStageIndex + 1;
        insertionIndex = Math.Clamp(insertionIndex, 0, Spec.Stages.Count - 1);

        _linearizer.MoveStage(Spec, sourceStageIndex, insertionIndex);
        CompleteStructuralEdit(() => SelectMovedStage(movedStage));
        return true;
    }

    private bool MoveStepToStageEnd(QuestGraphNode source, QuestGraphNode target)
    {
        if (target.StageIndex is not int targetStageIndex || Spec is null || !HasStage(targetStageIndex))
        {
            AddGenerationLogEntry("Cannot move step: target stage is stale.");
            return false;
        }

        return source.StageIndex is int sourceStageIndex
            && source.StepIndex is int sourceStepIndex
            && TryMoveStep(sourceStageIndex, sourceStepIndex, targetStageIndex, Spec.Stages[targetStageIndex].Steps.Count);
    }

    private bool MoveStepAfterStep(QuestGraphNode source, QuestGraphNode target)
    {
        if (Spec is null
            || source.StageIndex is not int sourceStageIndex
            || source.StepIndex is not int sourceStepIndex
            || target.StageIndex is not int targetStageIndex
            || target.StepIndex is not int targetStepIndex
            || !HasStep(sourceStageIndex, sourceStepIndex)
            || !HasStep(targetStageIndex, targetStepIndex))
        {
            AddGenerationLogEntry("Cannot move step: source or target step is stale.");
            return false;
        }

        if (sourceStageIndex == targetStageIndex && sourceStepIndex == targetStepIndex)
            return false;

        var insertionIndex = sourceStageIndex == targetStageIndex && sourceStepIndex < targetStepIndex
            ? targetStepIndex
            : targetStepIndex + 1;

        return TryMoveStep(sourceStageIndex, sourceStepIndex, targetStageIndex, insertionIndex);
    }

    private bool TryMoveStep(int fromStageIndex, int fromStepIndex, int toStageIndex, int toStepIndex)
    {
        if (Spec is null)
            return false;

        if (!HasStage(fromStageIndex))
        {
            AddGenerationLogEntry($"Cannot move step: source stage index {fromStageIndex} is not valid for {Spec.Stages.Count} stage(s).");
            return false;
        }

        if (!HasStage(toStageIndex))
        {
            AddGenerationLogEntry($"Cannot move step: target stage index {toStageIndex} is not valid for {Spec.Stages.Count} stage(s).");
            return false;
        }

        var fromStage = Spec.Stages[fromStageIndex];
        if (!HasStep(fromStageIndex, fromStepIndex))
        {
            AddGenerationLogEntry($"Cannot move step: step index {fromStepIndex} is not valid for source stage {fromStage.Number}.");
            return false;
        }

        var movedStep = fromStage.Steps[fromStepIndex];
        _linearizer.MoveStep(Spec, fromStageIndex, fromStepIndex, toStageIndex, toStepIndex);
        CompleteStructuralEdit(() => SelectMovedStep(movedStep));
        return true;
    }

    private void CompleteStructuralEdit(Action? selectNode)
    {
        IsDirty = true;
        RebuildGraph();
        selectNode?.Invoke();
        Definition = VisualEditorDefinitionBuilder.Build(Spec, SelectedNode);
        RefreshPreview();
    }

    private void SelectMovedStage(QuestStageSpec movedStage)
    {
        if (Spec is null)
            return;

        var stageIndex = Spec.Stages.FindIndex(stage => ReferenceEquals(stage, movedStage));
        SelectedNode = Graph.Nodes.FirstOrDefault(node =>
            node.Kind == QuestGraphNodeKind.Stage
            && node.StageIndex == stageIndex
            && !node.Id.EndsWith("-join", StringComparison.Ordinal));
    }

    private void SelectMovedStep(QuestStepSpec movedStep)
    {
        if (Spec is null)
            return;

        SelectedNode = Graph.Nodes.FirstOrDefault(node =>
            IsStepNode(node)
            && node.StageIndex is int stageIndex
            && node.StepIndex is int stepIndex
            && HasStep(stageIndex, stepIndex)
            && ReferenceEquals(Spec.Stages[stageIndex].Steps[stepIndex], movedStep));
    }

    private QuestGraphNode? FindNode(string nodeId)
    {
        return Graph.Nodes.FirstOrDefault(node => string.Equals(node.Id, nodeId, StringComparison.Ordinal));
    }

    private bool HasStage(int stageIndex)
    {
        return Spec is not null && stageIndex >= 0 && stageIndex < Spec.Stages.Count;
    }

    private bool HasStep(int stageIndex, int stepIndex)
    {
        return HasStage(stageIndex)
            && stepIndex >= 0
            && stepIndex < Spec!.Stages[stageIndex].Steps.Count;
    }

    private static bool IsStepNode(QuestGraphNode node)
    {
        return node.Kind is QuestGraphNodeKind.Step or QuestGraphNodeKind.RandomOptions;
    }

    private static string BuildWalkthrough(QuestSpec spec)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Quest start");
        builder.AppendLine(string.IsNullOrWhiteSpace(spec.Quest.StarterText)
            ? spec.Quest.Name
            : spec.Quest.StarterText);

        foreach (var stage in spec.Stages.OrderBy(stage => stage.Number))
        {
            builder.AppendLine();
            builder.AppendLine($"Stage {stage.Number}: {stage.Description}");
            builder.AppendLine($"Parallel: {stage.IsParallel}");

            foreach (var step in stage.Steps.OrderBy(step => step.Number))
                builder.AppendLine($"  Step {step.Number}: {step.Type} - {step.Description}");
        }

        builder.AppendLine();
        builder.AppendLine("Completion");
        builder.AppendLine(spec.Quest.CompletionText);
        return builder.ToString();
    }

    private static string FormatDiagnostic(QuestDiagnostic diagnostic)
    {
        return $"{diagnostic.Severity,-7} {diagnostic.SectionKey,-18} {diagnostic.Code,-24} {diagnostic.Message}";
    }
}
