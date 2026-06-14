using Avalonia.Controls;
using Avalonia.Media;
using System.Globalization;
using QuestParser.Core;

namespace QuestParser.Desktop;

public partial class VisualEditorWindow : Window
{
    private readonly VisualEditorViewModel _viewModel;
    private readonly bool _ownsSpec;

    public VisualEditorWindow()
        : this(new QuestWorkflow(), null, ownsSpec: true)
    {
    }

    public VisualEditorWindow(QuestWorkflow workflow, QuestSpec? spec, bool ownsSpec)
    {
        InitializeComponent();

        _viewModel = new VisualEditorViewModel(workflow, spec);
        _ownsSpec = ownsSpec;

        PopulatePalette();
        WireEvents();
        RefreshAll();
    }

    public QuestSpec? EditedSpec => _viewModel.Spec;

    private void PopulatePalette()
    {
        ActionPaletteList.ItemsSource = Enum.GetValues<StepType>()
            .Select(type => new PaletteItem(type.ToString(), type))
            .ToList();

        FlowPaletteList.ItemsSource = new List<PaletteItem>
        {
            new PaletteItem("Stage"),
            new PaletteItem("Parallel Stage"),
            new PaletteItem("Random Options"),
            new PaletteItem("Comment")
        };
    }

    private void WireEvents()
    {
        GraphCanvas.NodeSelected += nodeId =>
        {
            _viewModel.SelectNode(nodeId);
            RefreshInspector();
            RefreshCanvas();
        };

        GraphCanvas.NodeMoved += (nodeId, x, y) =>
        {
            _viewModel.MoveNode(nodeId, x, y);
            RefreshCanvas();
        };

        ValidateButton.Click += (_, _) => RefreshDiagnostics();
        SaveButton.Click += async (_, _) => await SaveAsync();
        ActionPaletteList.DoubleTapped += (_, _) => AddSelectedAction();
        FlowPaletteList.DoubleTapped += (_, _) => AddSelectedFlow();
        FormButton.Click += (_, _) => RefreshInspector();
        DefinitionButton.Click += (_, _) => ShowDefinition();
    }

    private async Task SaveAsync()
    {
        if (_viewModel.Spec is null)
        {
            RefreshEnabledState();
            return;
        }

        try
        {
            await _viewModel.SaveSpecAsync();
            RefreshAll();

            if (!_ownsSpec)
                Close(EditedSpec);
        }
        catch (Exception ex)
        {
            _viewModel.GenerationLog.Add("Save failed: " + ex.Message);
            RefreshBottomPanels();
            RefreshEnabledState();
        }
    }

    private void AddSelectedAction()
    {
        if (_viewModel.Spec is null)
        {
            RefreshEnabledState();
            return;
        }

        if (ActionPaletteList.SelectedItem is not PaletteItem { StepType: { } stepType })
            return;

        var stageIndex = _viewModel.SelectedNode?.StageIndex ?? 0;
        _viewModel.AddStep(stageIndex, stepType);
        RefreshAll();
    }

    private void AddSelectedFlow()
    {
        if (_viewModel.Spec is null)
        {
            RefreshEnabledState();
            return;
        }

        if (FlowPaletteList.SelectedItem is not PaletteItem item)
            return;

        switch (item.Label)
        {
            case "Stage":
                _viewModel.AddStage(isParallel: false);
                RefreshAll();
                break;
            case "Parallel Stage":
                _viewModel.AddStage(isParallel: true);
                RefreshAll();
                break;
            default:
                _viewModel.GenerationLog.Add($"Flow item '{item.Label}' is not wired yet.");
                RefreshBottomPanels();
                RefreshEnabledState();
                break;
        }
    }

    private void RefreshAll()
    {
        _viewModel.RefreshPreview();
        RefreshCanvas();
        BindDiagnostics();
        RefreshInspector();
        RefreshBottomPanels();
        RefreshEnabledState();
    }

    private void RefreshCanvas()
    {
        GraphCanvas.Graph = _viewModel.Graph;
        GraphCanvas.SelectedNodeId = _viewModel.SelectedNode?.Id ?? "";
    }

    private void RefreshDiagnostics()
    {
        _viewModel.Validate();
        BindDiagnostics();
    }

    private void BindDiagnostics()
    {
        DiagnosticsList.ItemsSource = _viewModel.Diagnostics;
    }

    private void RefreshEnabledState()
    {
        var hasSpec = _viewModel.Spec is not null;
        SaveButton.IsEnabled = hasSpec;
        ActionPaletteList.IsEnabled = hasSpec;
        FlowPaletteList.IsEnabled = hasSpec;
    }

    private void RefreshInspector()
    {
        InspectorPanel.Children.Clear();

        var spec = _viewModel.Spec;
        var selectedNode = _viewModel.SelectedNode;
        InspectorTitleText.Text = string.IsNullOrWhiteSpace(selectedNode?.Title)
            ? "Workflow"
            : selectedNode.Title;

        if (spec is null)
        {
            InspectorTitleText.Text = "Workflow";
            AddReadOnlyRow("State", "No quest loaded");
            AddReadOnlyRow("Details", BuildWorkflowDetails(), acceptsReturn: true);
            return;
        }

        if (selectedNode is not null
            && IsStepNode(selectedNode)
            && TryGetSelectedStep(spec, selectedNode, out _, out var step))
        {
            AddNodeHeaderRows(selectedNode);
            AddEditableMultilineRow("Description", step.Description, value => step.Description = value);
            AddEditableMultilineRow("Completed", step.CompletedDescription, value => step.CompletedDescription = value);
            if (selectedNode.Kind == QuestGraphNodeKind.RandomOptions || step.HasRandomOptions)
            {
                AddReadOnlyRow(
                    "Search",
                    "Random option targets are reviewed/generated from option entries and are not edited in this inspector yet.",
                    acceptsReturn: true);
            }
            else
            {
                AddEditableRow("Search", step.SearchText, value =>
                {
                    step.SearchText = value;
                    step.Target = ResolvedReference.Missing(QuestSpecFactory.KindForStepType(step.Type), value);
                });
            }
            AddEditableIntRow("Quantity", step.QuantityMax, value => step.QuantityMax = value);
            return;
        }

        if (selectedNode is not null && IsStepNode(selectedNode))
        {
            AddGenericNodeDetails(selectedNode);
            return;
        }

        if (selectedNode is not null
            && selectedNode.Kind == QuestGraphNodeKind.Stage
            && TryGetSelectedStage(spec, selectedNode, out var stage))
        {
            AddNodeHeaderRows(selectedNode);
            AddEditableMultilineRow("Stage text", stage.Description, value => stage.Description = value);
            AddEditableMultilineRow("Completed text", stage.CompletedDescription, value => stage.CompletedDescription = value);
            AddReadOnlyRow("Parallel", stage.IsParallel ? "Yes" : "No");
            return;
        }

        if (selectedNode is not null && selectedNode.Kind == QuestGraphNodeKind.Stage)
        {
            AddGenericNodeDetails(selectedNode);
            return;
        }

        AddQuestRows(spec, selectedNode);
    }

    private void ShowDefinition()
    {
        InspectorPanel.Children.Clear();
        InspectorTitleText.Text = string.IsNullOrWhiteSpace(_viewModel.SelectedNode?.Title)
            ? "Definition"
            : _viewModel.SelectedNode.Title;

        var definitionBox = new TextBox
        {
            Text = _viewModel.Definition,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            MinHeight = 420
        };
        definitionBox.Classes.Add("mono-box");
        InspectorPanel.Children.Add(definitionBox);
    }

    private void RefreshBottomPanels()
    {
        WalkthroughBox.Text = _viewModel.Walkthrough;
        LuaPreviewBox.Text = _viewModel.LuaPreview;
        SqlPreviewBox.Text = _viewModel.SqlPreview;
        MissingPreviewBox.Text = _viewModel.MissingPreview;
        GenerationLogList.ItemsSource = _viewModel.GenerationLog;
    }

    private void AddReadOnlyRow(string label, string value, bool acceptsReturn = false)
    {
        InspectorPanel.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeight.SemiBold
        });

        var textBox = new TextBox
        {
            Text = value,
            IsReadOnly = true,
            AcceptsReturn = acceptsReturn,
            TextWrapping = acceptsReturn ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = acceptsReturn ? 96 : 32
        };

        InspectorPanel.Children.Add(textBox);
    }

    private void AddEditableRow(string label, string value, Action<string> save)
    {
        AddEditableRow(label, value, save, multiline: false);
    }

    private void AddEditableMultilineRow(string label, string value, Action<string> save)
    {
        AddEditableRow(label, value, save, multiline: true);
    }

    private void AddEditableRow(string label, string value, Action<string> save, bool multiline)
    {
        InspectorPanel.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeight.SemiBold
        });

        var currentValue = value ?? "";
        var textBox = new TextBox
        {
            Text = currentValue,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 96 : 32
        };

        textBox.LostFocus += (_, _) =>
        {
            var newValue = textBox.Text ?? "";
            if (string.Equals(newValue, currentValue, StringComparison.Ordinal))
                return;

            ApplyInspectorEdit(() => save(newValue));
            currentValue = newValue;
        };

        InspectorPanel.Children.Add(textBox);
    }

    private void AddEditableIntRow(string label, int value, Action<int> save)
    {
        InspectorPanel.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeight.SemiBold
        });

        var currentValue = value;
        var textBox = new TextBox
        {
            Text = currentValue.ToString(CultureInfo.InvariantCulture),
            MinHeight = 32
        };

        textBox.LostFocus += (_, _) =>
        {
            var text = (textBox.Text ?? "").Trim();
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var newValue)
                || newValue < 1)
            {
                textBox.Text = currentValue.ToString(CultureInfo.InvariantCulture);
                return;
            }

            if (newValue == currentValue)
                return;

            ApplyInspectorEdit(() => save(newValue));
            currentValue = newValue;
        };

        InspectorPanel.Children.Add(textBox);
    }

    private void ApplyInspectorEdit(Action edit)
    {
        edit();
        SyncGraphNodeSummaries();
        _viewModel.RefreshPreview();
        RefreshCanvas();
        BindDiagnostics();
        RefreshBottomPanels();
        RefreshEnabledState();
    }

    private void AddQuestRows(QuestSpec spec, QuestGraphNode? selectedNode)
    {
        if (selectedNode is null)
        {
            AddReadOnlyRow("Selected", "Workflow");
            AddReadOnlyRow("Kind", "Workflow");
        }
        else
        {
            AddNodeHeaderRows(selectedNode);
        }

        AddEditableRow("Quest name", spec.Quest.Name, value => spec.Quest.Name = value);
        AddEditableRow("Zone", spec.Quest.Zone, value => spec.Quest.Zone = value);
        AddEditableMultilineRow("Starter text", spec.Quest.StarterText, value => spec.Quest.StarterText = value);
        AddEditableMultilineRow("Completion text", spec.Quest.CompletionText, value => spec.Quest.CompletionText = value);
    }

    private void AddNodeHeaderRows(QuestGraphNode selectedNode)
    {
        AddReadOnlyRow("Selected", selectedNode.Id);
        AddReadOnlyRow("Kind", selectedNode.Kind.ToString());
    }

    private void AddGenericNodeDetails(QuestGraphNode selectedNode)
    {
        AddNodeHeaderRows(selectedNode);
        AddReadOnlyRow("Details", BuildNodeDetails(selectedNode), acceptsReturn: true);
    }

    private void SyncGraphNodeSummaries()
    {
        var spec = _viewModel.Spec;
        if (spec is null)
            return;

        foreach (var node in _viewModel.Graph.Nodes)
        {
            switch (node.Kind)
            {
                case QuestGraphNodeKind.Start:
                    node.Subtitle = spec.Quest.Name;
                    break;
                case QuestGraphNodeKind.Complete:
                    node.Subtitle = spec.Quest.CompletionText;
                    break;
                case QuestGraphNodeKind.Stage:
                    if (TryGetSelectedStage(spec, node, out var stage))
                    {
                        node.StageNumber = stage.Number;
                        node.IsParallelStage = stage.IsParallel;
                        node.Title = node.Id.EndsWith("-join", StringComparison.Ordinal)
                            ? $"Stage {stage.Number} complete"
                            : $"Stage {stage.Number}";
                        node.Subtitle = node.Id.EndsWith("-join", StringComparison.Ordinal)
                            ? stage.CompletedDescription
                            : stage.Description;
                    }
                    break;
                case QuestGraphNodeKind.Step:
                case QuestGraphNodeKind.RandomOptions:
                    if (TryGetSelectedStep(spec, node, out var parentStage, out var step))
                    {
                        node.StageNumber = parentStage.Number;
                        node.StepNumber = step.Number;
                        node.StepType = step.Type;
                        node.Title = $"{step.Type} Step {step.Number}";
                        node.Subtitle = step.Description;
                        node.RandomOptionCount = step.RandomOptions.Count;
                    }
                    break;
            }
        }
    }

    private static bool TryGetSelectedStage(QuestSpec spec, QuestGraphNode selectedNode, out QuestStageSpec stage)
    {
        stage = null!;

        if (selectedNode.StageIndex is not int stageIndex
            || stageIndex < 0
            || stageIndex >= spec.Stages.Count)
        {
            return false;
        }

        stage = spec.Stages[stageIndex];
        return true;
    }

    private static bool TryGetSelectedStep(
        QuestSpec spec,
        QuestGraphNode selectedNode,
        out QuestStageSpec stage,
        out QuestStepSpec step)
    {
        stage = null!;
        step = null!;

        if (!TryGetSelectedStage(spec, selectedNode, out stage)
            || selectedNode.StepIndex is not int stepIndex
            || stepIndex < 0
            || stepIndex >= stage.Steps.Count)
        {
            return false;
        }

        step = stage.Steps[stepIndex];
        return true;
    }

    private static bool IsStepNode(QuestGraphNode selectedNode)
    {
        return selectedNode.Kind is QuestGraphNodeKind.Step or QuestGraphNodeKind.RandomOptions;
    }

    private string BuildWorkflowDetails()
    {
        var spec = _viewModel.Spec;
        if (spec is null)
            return "No quest loaded.";

        return string.Join(
            Environment.NewLine,
            $"Quest: {spec.Quest.Name}",
            $"Zone: {spec.Quest.Zone}",
            $"Stages: {spec.Stages.Count}",
            $"Steps: {spec.Stages.Sum(stage => stage.Steps.Count)}");
    }

    private static string BuildNodeDetails(QuestGraphNode node)
    {
        var details = new List<string>();

        if (!string.IsNullOrWhiteSpace(node.Title))
            details.Add("Title: " + node.Title);

        if (!string.IsNullOrWhiteSpace(node.Subtitle))
            details.Add("Subtitle: " + node.Subtitle);

        if (node.StageNumber is int stageNumber)
            details.Add("Stage: " + stageNumber);

        if (node.StepNumber is int stepNumber)
            details.Add("Step: " + stepNumber);

        if (node.StepType is StepType stepType)
            details.Add("Step type: " + stepType);

        if (node.IsParallelStage)
            details.Add("Parallel stage: True");

        if (node.RandomOptionCount > 0)
            details.Add("Random options: " + node.RandomOptionCount);

        return details.Count == 0
            ? node.Subtitle
            : string.Join(Environment.NewLine, details);
    }

    private sealed record PaletteItem(string Label, StepType? StepType = null)
    {
        public override string ToString() => Label;
    }
}
