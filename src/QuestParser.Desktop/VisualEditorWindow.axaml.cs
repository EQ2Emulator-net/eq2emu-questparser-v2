using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia;
using System.Globalization;
using System.Text.Json;
using QuestParser.Core;

namespace QuestParser.Desktop;

public partial class VisualEditorWindow : Window
{
    private const double MinimumCanvasWidth = 1800;
    private const double MinimumCanvasHeight = 1400;
    private const double CanvasMargin = 240;
    private const double MinimumZoom = 0.35;
    private const double MaximumZoom = 2.5;
    private const double ZoomStep = 1.2;

    private readonly VisualEditorViewModel _viewModel;
    private readonly bool _ownsSpec;
    private readonly Stack<string> _undoStack = new();
    private readonly Stack<string> _redoStack = new();
    private double _zoom = 1;
    private bool _busy;
    private bool _connectMode;
    private string? _connectionSourceNodeId;

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
        GraphCanvas.NodeSelected += HandleGraphNodeSelected;

        GraphCanvas.NodeMoveStarted += _ => PushUndoSnapshot();
        GraphCanvas.NodeMoved += (nodeId, x, y) =>
        {
            _viewModel.MoveNode(nodeId, x, y);
            RefreshCanvas();
        };

        UndoButton.Click += (_, _) => Undo();
        RedoButton.Click += (_, _) => Redo();
        DeleteButton.Click += (_, _) => DeleteSelectedNode();
        ConnectButton.Click += (_, _) => ToggleConnectMode();
        ZoomInButton.Click += (_, _) => SetZoom(_zoom * ZoomStep);
        ZoomOutButton.Click += (_, _) => SetZoom(_zoom / ZoomStep);
        CenterButton.Click += (_, _) => CenterGraph();
        ValidateButton.Click += (_, _) => RefreshDiagnostics();
        OpenButton.Click += async (_, _) => await OpenSpecAsync();
        GenerateButton.Click += async (_, _) => await GenerateAsync();
        SaveButton.Click += async (_, _) => await SaveAsync();
        ActionPaletteList.DoubleTapped += (_, _) => AddSelectedAction();
        FlowPaletteList.DoubleTapped += (_, _) => AddSelectedFlow();
        FormButton.Click += (_, _) => RefreshInspector();
        DefinitionButton.Click += (_, _) => ShowDefinition();
        KeyDown += (_, e) => HandleWindowKeyDown(e);
    }

    private void HandleGraphNodeSelected(string nodeId)
    {
        if (_connectMode)
        {
            HandleConnectNodeSelected(nodeId);
            return;
        }

        _viewModel.SelectNode(nodeId);
        RefreshInspector();
        RefreshCanvas();
        RefreshEnabledState();
    }

    private void HandleConnectNodeSelected(string nodeId)
    {
        if (_connectionSourceNodeId is null)
        {
            _viewModel.SelectNode(nodeId);
            _connectionSourceNodeId = nodeId;
            _viewModel.GenerationLog.Add($"Connect source selected: {nodeId}. Select a target node.");
            RefreshInspector();
            RefreshCanvas();
            RefreshBottomPanels();
            RefreshEnabledState();
            return;
        }

        var sourceNodeId = _connectionSourceNodeId;
        ClearConnectMode();
        ApplyStructuralEdit(() => _viewModel.ConnectNodes(sourceNodeId, nodeId));
    }

    private void ToggleConnectMode()
    {
        if (_connectMode)
        {
            ClearConnectMode();
            return;
        }

        if (_viewModel.Spec is null)
        {
            RefreshEnabledState();
            return;
        }

        _connectMode = true;
        _connectionSourceNodeId = null;
        _viewModel.GenerationLog.Add("Connect mode: select a source node, then a target node.");
        RefreshCanvas();
        RefreshBottomPanels();
        RefreshEnabledState();
    }

    private void ClearConnectMode()
    {
        _connectMode = false;
        _connectionSourceNodeId = null;
        RefreshCanvas();
        RefreshEnabledState();
    }

    private void DeleteSelectedNode()
    {
        var selectedNodeId = _viewModel.SelectedNode?.Id;
        if (string.IsNullOrWhiteSpace(selectedNodeId))
        {
            RefreshEnabledState();
            return;
        }

        ClearConnectMode();
        ApplyStructuralEdit(() => _viewModel.DeleteNode(selectedNodeId));
    }

    private void HandleWindowKeyDown(KeyEventArgs e)
    {
        if (e.Source is TextBox or ComboBox)
            return;

        if (e.Key is Key.Delete or Key.Back)
        {
            DeleteSelectedNode();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _connectMode)
        {
            ClearConnectMode();
            e.Handled = true;
        }
    }

    private async Task OpenSpecAsync()
    {
        if (!TryBeginBusy())
            return;

        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider.CanOpen != true)
            {
                _viewModel.GenerationLog.Add("Open spec failed: file picker is not available.");
                RefreshBottomPanels();
                return;
            }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open quest spec",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Quest spec")
                    {
                        Patterns = ["*.quest.json"]
                    }
                ]
            });

            if (files.Count == 0)
                return;

            var selectedPath = files[0].Path;
            if (!selectedPath.IsFile)
            {
                _viewModel.GenerationLog.Add($"Open spec failed: selected file is not local: {selectedPath}");
                RefreshBottomPanels();
                return;
            }

            var path = selectedPath.LocalPath;
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            {
                _viewModel.GenerationLog.Add($"Open spec failed: selected file does not exist: {path}");
                RefreshBottomPanels();
                return;
            }

            var spec = await QuestWorkflow.ReadSpecAsync(path);
            _viewModel.LoadSpec(spec);
            ClearUndoHistory();
            _viewModel.GenerationLog.Add($"Loaded {path}");
            RefreshAll();
        }
        catch (Exception ex)
        {
            _viewModel.GenerationLog.Add("Open spec failed: " + ex.Message);
            RefreshBottomPanels();
            RefreshEnabledState();
        }
        finally
        {
            _busy = false;
            RefreshEnabledState();
        }
    }

    private async Task GenerateAsync()
    {
        if (_viewModel.Spec is null)
        {
            _viewModel.GenerationLog.Add("Generate skipped: no spec is loaded.");
            RefreshBottomPanels();
            RefreshEnabledState();
            return;
        }

        if (!_ownsSpec)
        {
            _viewModel.GenerationLog.Add("Generate skipped: integrated visual editor changes must be saved before generation.");
            RefreshBottomPanels();
            RefreshEnabledState();
            return;
        }

        if (!TryBeginBusy())
            return;

        try
        {
            var result = await _viewModel.GenerateAsync(overwrite: false);
            if (result is null)
                return;

            foreach (var file in result.WrittenFiles)
                _viewModel.GenerationLog.Add($"Generated {file}");

            RefreshAll();
        }
        catch (Exception ex)
        {
            _viewModel.GenerationLog.Add("Generate failed: " + ex.Message);
            RefreshBottomPanels();
            RefreshEnabledState();
        }
        finally
        {
            _busy = false;
            RefreshEnabledState();
        }
    }

    private async Task SaveAsync()
    {
        if (_viewModel.Spec is null)
        {
            RefreshEnabledState();
            return;
        }

        if (!TryBeginBusy())
            return;

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
        finally
        {
            _busy = false;
            RefreshEnabledState();
        }
    }

    private bool TryBeginBusy()
    {
        if (_busy)
            return false;

        _busy = true;
        RefreshEnabledState();
        return true;
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
        ApplyStructuralEdit(() => _viewModel.AddStep(stageIndex, stepType));
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
                ApplyStructuralEdit(() => _viewModel.AddStage(isParallel: false));
                break;
            case "Parallel Stage":
                ApplyStructuralEdit(() => _viewModel.AddStage(isParallel: true));
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
        GraphCanvas.Zoom = _zoom;
        GraphCanvas.Graph = _viewModel.Graph;
        GraphCanvas.SelectedNodeId = _viewModel.SelectedNode?.Id ?? "";
        GraphCanvas.ConnectionSourceNodeId = _connectionSourceNodeId ?? "";
        UpdateGraphCanvasExtent();
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
        var isIdle = !_busy;
        UndoButton.IsEnabled = isIdle && _undoStack.Count > 0;
        RedoButton.IsEnabled = isIdle && _redoStack.Count > 0;
        DeleteButton.IsEnabled = isIdle && CanDeleteNode(_viewModel.SelectedNode);
        ConnectButton.IsEnabled = hasSpec && isIdle;
        ConnectButton.Content = _connectMode ? "Cancel edit" : "Edit connections";
        ZoomInButton.IsEnabled = isIdle && _zoom < MaximumZoom;
        ZoomOutButton.IsEnabled = isIdle && _zoom > MinimumZoom;
        CenterButton.IsEnabled = isIdle && hasSpec;
        ValidateButton.IsEnabled = isIdle;
        OpenButton.IsEnabled = isIdle;
        GenerateButton.IsEnabled = hasSpec && isIdle && _ownsSpec;
        SaveButton.IsEnabled = hasSpec && isIdle;
        FormButton.IsEnabled = isIdle;
        DefinitionButton.IsEnabled = isIdle;
        ActionPaletteList.IsEnabled = hasSpec && isIdle;
        FlowPaletteList.IsEnabled = hasSpec && isIdle;
    }

    private void PushUndoSnapshot()
    {
        var snapshot = CaptureSnapshot();
        if (snapshot is null)
            return;

        if (_undoStack.Count == 0 || !string.Equals(_undoStack.Peek(), snapshot, StringComparison.Ordinal))
            _undoStack.Push(snapshot);
        _redoStack.Clear();
        RefreshEnabledState();
    }

    private void ClearUndoHistory()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        RefreshEnabledState();
    }

    private void Undo()
    {
        if (_undoStack.Count == 0)
            return;

        var current = CaptureSnapshot();
        var previous = _undoStack.Pop();
        if (current is not null)
            _redoStack.Push(current);

        RestoreSnapshot(previous);
    }

    private void Redo()
    {
        if (_redoStack.Count == 0)
            return;

        var current = CaptureSnapshot();
        var next = _redoStack.Pop();
        if (current is not null)
            _undoStack.Push(current);

        RestoreSnapshot(next);
    }

    private string? CaptureSnapshot()
    {
        return _viewModel.Spec is null
            ? null
            : JsonSerializer.Serialize(_viewModel.Spec, QuestSpecJsonContext.Default.QuestSpec);
    }

    private void RestoreSnapshot(string snapshot)
    {
        var selectedNodeId = _viewModel.SelectedNode?.Id;
        var spec = JsonSerializer.Deserialize(snapshot, QuestSpecJsonContext.Default.QuestSpec);
        if (spec is null)
            return;

        _viewModel.LoadSpec(spec);
        if (!string.IsNullOrWhiteSpace(selectedNodeId))
            _viewModel.SelectNode(selectedNodeId);
        _viewModel.MarkDirty();
        RefreshAll();
    }

    private void SetZoom(double zoom)
    {
        var nextZoom = Math.Clamp(zoom, MinimumZoom, MaximumZoom);
        if (Math.Abs(nextZoom - _zoom) < 0.001)
            return;

        var viewport = GraphScrollViewer.Viewport;
        var centerX = (GraphScrollViewer.Offset.X + viewport.Width / 2) / _zoom;
        var centerY = (GraphScrollViewer.Offset.Y + viewport.Height / 2) / _zoom;

        _zoom = nextZoom;
        RefreshCanvas();
        GraphScrollViewer.Offset = ClampGraphOffset(new Vector(
            centerX * _zoom - viewport.Width / 2,
            centerY * _zoom - viewport.Height / 2));
        RefreshEnabledState();
    }

    private void CenterGraph()
    {
        var contentBounds = GetGraphContentBounds(_viewModel.Graph);
        var graphOffset = GraphCanvas.GraphOffset;
        var viewport = GraphScrollViewer.Viewport;
        var offset = new Vector(
            (contentBounds.X + contentBounds.Width / 2 + graphOffset.X) * _zoom - viewport.Width / 2,
            (contentBounds.Y + contentBounds.Height / 2 + graphOffset.Y) * _zoom - viewport.Height / 2);

        GraphScrollViewer.Offset = ClampGraphOffset(offset);
    }

    private Vector ClampGraphOffset(Vector offset)
    {
        var maxX = Math.Max(0, GraphCanvas.Width - GraphScrollViewer.Viewport.Width);
        var maxY = Math.Max(0, GraphCanvas.Height - GraphScrollViewer.Viewport.Height);
        return new Vector(
            Math.Clamp(offset.X, 0, maxX),
            Math.Clamp(offset.Y, 0, maxY));
    }

    private void UpdateGraphCanvasExtent()
    {
        var viewport = CalculateGraphCanvasViewport(_viewModel.Graph, _zoom);
        GraphCanvas.GraphOffset = viewport.GraphOffset;
        GraphCanvas.Width = viewport.Width;
        GraphCanvas.Height = viewport.Height;
    }

    internal static GraphCanvasViewport CalculateGraphCanvasViewport(QuestGraph graph, double zoom)
    {
        var contentBounds = GetGraphContentBounds(graph);
        if (graph.Nodes.Count == 0)
        {
            return new GraphCanvasViewport(
                Math.Ceiling(MinimumCanvasWidth * zoom),
                Math.Ceiling(MinimumCanvasHeight * zoom),
                default);
        }

        var graphOffset = new Vector(
            contentBounds.X < CanvasMargin ? CanvasMargin - contentBounds.X : 0,
            contentBounds.Y < CanvasMargin ? CanvasMargin - contentBounds.Y : 0);
        var logicalWidth = Math.Max(MinimumCanvasWidth, contentBounds.Right + graphOffset.X + CanvasMargin);
        var logicalHeight = Math.Max(MinimumCanvasHeight, contentBounds.Bottom + graphOffset.Y + CanvasMargin);

        return new GraphCanvasViewport(
            Math.Ceiling(logicalWidth * zoom),
            Math.Ceiling(logicalHeight * zoom),
            graphOffset);
    }

    private static Rect GetGraphContentBounds(QuestGraph graph)
    {
        var nodes = graph.Nodes;
        if (nodes.Count == 0)
            return new Rect(0, 0, MinimumCanvasWidth - CanvasMargin, MinimumCanvasHeight - CanvasMargin);

        var hasBounds = false;
        var minX = 0d;
        var minY = 0d;
        var maxX = 0d;
        var maxY = 0d;

        foreach (var node in nodes)
        {
            var bounds = GetNodeBounds(node);
            if (!hasBounds)
            {
                minX = bounds.X;
                minY = bounds.Y;
                maxX = bounds.Right;
                maxY = bounds.Bottom;
                hasBounds = true;
                continue;
            }

            minX = Math.Min(minX, bounds.X);
            minY = Math.Min(minY, bounds.Y);
            maxX = Math.Max(maxX, bounds.Right);
            maxY = Math.Max(maxY, bounds.Bottom);
        }

        return new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
    }

    private static Rect GetNodeBounds(QuestGraphNode node)
    {
        var layout = node.Layout;
        var width = layout?.Width > 0 ? layout.Width : 260;
        var height = layout?.Height > 0 ? layout.Height : 72;
        return new Rect(layout?.X ?? 0, layout?.Y ?? 0, width, height);
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
            if (selectedNode.StageIndex is int fromStageIndex
                && selectedNode.StepIndex is int fromStepIndex)
            {
                AddStagePickerRow(
                    "Stage",
                    spec,
                    fromStageIndex,
                    toStageIndex => ApplyStructuralEdit(() => _viewModel.MoveStepToStage(fromStageIndex, fromStepIndex, toStageIndex)));
            }
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
            if (selectedNode.StageIndex is int stageIndex)
            {
                AddYesNoRow(
                    "Parallel",
                    stage.IsParallel,
                    isParallel => ApplyStructuralEdit(() => _viewModel.SetStageParallel(stageIndex, isParallel)));
            }
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
            IsEnabled = false,
            AcceptsReturn = acceptsReturn,
            TextWrapping = acceptsReturn ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = acceptsReturn ? 96 : 32
        };

        InspectorPanel.Children.Add(textBox);
    }

    private void AddYesNoRow(string label, bool value, Action<bool> save)
    {
        InspectorPanel.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeight.SemiBold
        });

        var currentValue = value;
        var comboBox = new ComboBox
        {
            ItemsSource = new[] { "No", "Yes" },
            SelectedIndex = value ? 1 : 0,
            MinHeight = 32
        };

        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedIndex < 0)
                return;

            var newValue = comboBox.SelectedIndex == 1;
            if (newValue == currentValue)
                return;

            save(newValue);
            currentValue = newValue;
        };

        InspectorPanel.Children.Add(comboBox);
    }

    private void AddStagePickerRow(string label, QuestSpec spec, int currentStageIndex, Action<int> save)
    {
        InspectorPanel.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeight.SemiBold
        });

        var choices = spec.Stages
            .Select((stage, index) => new StageChoice(index, $"Stage {stage.Number}: {TrimForPicker(stage.Description)}"))
            .ToList();
        var currentChoice = choices.FirstOrDefault(choice => choice.Index == currentStageIndex);
        var comboBox = new ComboBox
        {
            ItemsSource = choices,
            SelectedItem = currentChoice,
            MinHeight = 32
        };

        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is not StageChoice choice || choice.Index == currentStageIndex)
                return;

            save(choice.Index);
        };

        InspectorPanel.Children.Add(comboBox);
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
        PushUndoSnapshot();
        edit();
        _viewModel.MarkDirty();
        SyncGraphNodeSummaries();
        _viewModel.RefreshPreview();
        RefreshCanvas();
        BindDiagnostics();
        RefreshBottomPanels();
        RefreshEnabledState();
    }

    private void ApplyStructuralEdit(Action edit)
    {
        PushUndoSnapshot();
        edit();
        RefreshAll();
    }

    private void ApplyStructuralEdit(Func<bool> edit)
    {
        var before = CaptureSnapshot();
        var changed = edit();
        var after = CaptureSnapshot();

        if (changed
            && before is not null
            && after is not null
            && !string.Equals(before, after, StringComparison.Ordinal))
        {
            if (_undoStack.Count == 0 || !string.Equals(_undoStack.Peek(), before, StringComparison.Ordinal))
                _undoStack.Push(before);
            _redoStack.Clear();
        }

        RefreshAll();
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

    private static bool CanDeleteNode(QuestGraphNode? selectedNode)
    {
        return selectedNode is not null
            && (selectedNode.Kind == QuestGraphNodeKind.Stage || IsStepNode(selectedNode));
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

    private static string TrimForPicker(string? value)
    {
        value = (value ?? "").Trim();
        if (value.Length == 0)
            return "Untitled";
        return value.Length <= 56 ? value : value[..53] + "...";
    }

    private sealed record PaletteItem(string Label, StepType? StepType = null)
    {
        public override string ToString() => Label;
    }

    private sealed record StageChoice(int Index, string Label)
    {
        public override string ToString() => Label;
    }

    internal readonly record struct GraphCanvasViewport(double Width, double Height, Vector GraphOffset);
}
