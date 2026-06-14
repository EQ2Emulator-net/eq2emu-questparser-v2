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

        _linearizer.AddStep(Spec, stageIndex, stepType);
        IsDirty = true;
        RebuildGraph();
        RefreshPreview();
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
            LuaPreview = "";
            SqlPreview = "";
            MissingPreview = "";
            Walkthrough = "";
            Definition = "";
            Diagnostics.Clear();
            return;
        }

        var diagnostics = Validate();
        Walkthrough = BuildWalkthrough(Spec);
        Definition = VisualEditorDefinitionBuilder.Build(Spec, SelectedNode);

        if (diagnostics.Any(diagnostic => diagnostic.Severity == QuestDiagnosticSeverity.Blocker)
            && !string.IsNullOrEmpty(LuaPreview))
        {
            GenerationLog.Add("Preview not refreshed because diagnostics contain blockers; stale generated output is still shown.");
            return;
        }

        try
        {
            var preview = _workflow.Preview(Spec);
            LuaPreview = preview.Lua;
            SqlPreview = preview.Sql;
            MissingPreview = preview.MissingReport;
            Walkthrough = BuildWalkthrough(Spec);
            Definition = VisualEditorDefinitionBuilder.Build(Spec, SelectedNode);
        }
        catch (Exception ex)
        {
            GenerationLog.Add("Preview failed: " + ex.Message);
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
