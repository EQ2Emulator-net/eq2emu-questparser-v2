using Avalonia.Controls;
using Avalonia.Media;
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
        }
    }

    private void AddSelectedAction()
    {
        if (ActionPaletteList.SelectedItem is not PaletteItem { StepType: { } stepType })
            return;

        var stageIndex = _viewModel.SelectedNode?.StageIndex ?? 0;
        _viewModel.AddStep(stageIndex, stepType);
        RefreshAll();
    }

    private void AddSelectedFlow()
    {
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
        }
    }

    private void RefreshAll()
    {
        _viewModel.RefreshPreview();
        RefreshCanvas();
        RefreshDiagnostics();
        RefreshInspector();
        RefreshBottomPanels();
    }

    private void RefreshCanvas()
    {
        GraphCanvas.Graph = _viewModel.Graph;
        GraphCanvas.SelectedNodeId = _viewModel.SelectedNode?.Id ?? "";
    }

    private void RefreshDiagnostics()
    {
        _viewModel.Validate();
        DiagnosticsList.ItemsSource = _viewModel.Diagnostics;
    }

    private void RefreshInspector()
    {
        InspectorPanel.Children.Clear();

        var selectedNode = _viewModel.SelectedNode;
        InspectorTitleText.Text = string.IsNullOrWhiteSpace(selectedNode?.Title)
            ? "Workflow"
            : selectedNode.Title;

        if (selectedNode is null)
        {
            AddReadOnlyRow("Selected", "Workflow");
            AddReadOnlyRow("Kind", "Workflow");
            AddReadOnlyRow("Details", BuildWorkflowDetails(), acceptsReturn: true);
            return;
        }

        AddReadOnlyRow("Selected", selectedNode.Id);
        AddReadOnlyRow("Kind", selectedNode.Kind.ToString());
        AddReadOnlyRow("Details", BuildNodeDetails(selectedNode), acceptsReturn: true);
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

    private string BuildWorkflowDetails()
    {
        var spec = _viewModel.Spec;
        if (spec is null)
            return "No quest spec loaded.";

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
