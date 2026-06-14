using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using QuestParser.Core;

namespace QuestParser.Desktop;

public partial class MainWindow : Window
{
    private QuestWorkflow _workflow = new();
    private readonly List<ReviewSection> _sections = [];
    private readonly HashSet<string> _verifiedSections = [];
    private readonly HashSet<string> _dirtyEditorKeys = [];
    private readonly Dictionary<string, Control> _editors = [];
    private readonly ObservableCollection<SectionDisplay> _sectionRows = [];
    private readonly ObservableCollection<string> _sourceRows = [];
    private readonly ObservableCollection<CandidateDisplay> _candidateRows = [];
    private readonly ObservableCollection<string> _diagnosticRows = [];

    private QuestSpec? _spec;
    private ReviewSection? _currentSection;
    private QuestParserUiSettings _settings = QuestParserUiSettings.Load();
    private bool _loadingSection;
    private bool _refreshingSectionList;
    private bool _selectingSection;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();

        SectionList.ItemsSource = _sectionRows;
        SourceList.ItemsSource = _sourceRows;
        CandidateList.ItemsSource = _candidateRows;
        DiagnosticsList.ItemsSource = _diagnosticRows;

        RefreshSettingsSummary();
        ApplyUiSettings();
        _workflow = CreateWorkflowFromSettings();
        TemplateBox.ItemsSource = Enum.GetValues<QuestTemplateKind>().Select(kind => new TemplateChoice(kind)).ToArray();
        TemplateBox.SelectedIndex = 0;

        WireActions();
        ClearLoadedQuestState();
        AppendLog("Ready. Fetch from the configured quest source, load an existing spec, or create a manual template.");
    }

    private void WireActions()
    {
        SettingsMenuItem.Click += async (_, _) => await OpenSettingsAsync();
        VisualEditorMenuItem.Click += async (_, _) => await OpenVisualEditorAsync();
        LayoutSettingsMenuItem.Click += async (_, _) => await OpenSettingsAsync();
        FetchButton.Click += async (_, _) => await RunAsync("Fetch + resolve", FetchAndResolveAsync);
        NewTemplateButton.Click += (_, _) => RunSync("Create template", CreateTemplateQuest);
        PreviewSpecButton.Click += async (_, _) => await RunAsync("Preview spec", LoadSpecPreviewAsync);

        GenerateButton.Click += async (_, _) => await RunAsync("Generate files", GenerateFilesAsync);
        ResolveSectionButton.Click += async (_, _) => await RunAsync("Resolve section", ResolveCurrentSectionAsync);
        VerifyButton.Click += (_, _) => RunSync("Verify section", VerifyCurrentSection);
        PreviousButton.Click += (_, _) => MoveSection(-1);
        NextButton.Click += (_, _) => MoveSection(1);

        SectionList.SelectionChanged += (_, _) =>
        {
            if (_refreshingSectionList || _selectingSection || SectionList.SelectedIndex < 0)
                return;

            var selectedIndex = SectionList.SelectedIndex;
            if (selectedIndex == CurrentSectionIndex())
                return;

            SelectSectionFromSidebar(selectedIndex);
        };

        CandidateList.SelectionChanged += (_, _) => RefreshActionStates();
        UseCandidateButton.Click += (_, _) => RunSync("Use candidate", UseSelectedCandidate);
    }

    private async Task RunAsync(string actionName, Func<Task> action)
    {
        var runningStatus = actionName + "...";
        try
        {
            SetBusy(true, runningStatus);
            await action();
            if (StatusText.Text == runningStatus)
                SetStatus(actionName + " complete.");
        }
        catch (Exception ex)
        {
            SetStatus(actionName + " failed.");
            AppendLog(ex.ToString());
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RunSync(string actionName, Action action)
    {
        var runningStatus = actionName + "...";
        try
        {
            SetBusy(true, runningStatus);
            action();
            if (StatusText.Text == runningStatus)
                SetStatus(actionName + " complete.");
        }
        catch (Exception ex)
        {
            SetStatus(actionName + " failed.");
            AppendLog(ex.ToString());
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task FetchAndResolveAsync()
    {
        var questName = RequiredQuestName();
        _workflow = CreateWorkflowFromSettings();

        ClearLoadedQuestState();
        AppendLog($"Fetching quest source data for '{questName}'.");
        var imported = await _workflow.ImportAsync(
            questName,
            CleanPath(_settings.ContentRoot, Defaults.ContentRoot),
            (AuthorBox.Text ?? "").Trim());

        AppendLog($"Quest source import created spec: {imported.Spec.Output.SpecPath}");
        AppendLog("Resolving DB references.");
        var resolved = await _workflow.ResolveAsync(imported.Spec.Output.SpecPath);

        _spec = resolved.Spec;
        ApplySettingsGenerationModeToSpec();
        QuestNameBox.Text = _spec.Quest.Name;
        AuthorBox.Text = _spec.Quest.Author;
        _settings = _settings with { ContentRoot = _spec.Output.ContentRoot };
        RefreshSettingsSummary();
        SpecPathBox.Text = _spec.Output.SpecPath;

        await LoadRawCensusTabsAsync(questName);
        RebuildSections();
        SetWorkflowEnabled(true);
        SelectSection(0);
        RefreshPreview();
        AppendLog("Review is ready. Verify each section before generating files.");
    }

    private void CreateTemplateQuest()
    {
        var questName = RequiredQuestName();
        var choice = TemplateBox.SelectedItem as TemplateChoice ?? new TemplateChoice(QuestTemplateKind.Blank);

        ClearLoadedQuestState();
        var result = _workflow.CreateTemplate(
            choice.Kind,
            questName,
            "Uncategorized",
            CleanPath(_settings.ContentRoot, Defaults.ContentRoot),
            (AuthorBox.Text ?? "").Trim());

        _spec = result.Spec;
        ApplySettingsGenerationModeToSpec();
        QuestNameBox.Text = _spec.Quest.Name;
        AuthorBox.Text = _spec.Quest.Author;
        _settings = _settings with { ContentRoot = _spec.Output.ContentRoot };
        RefreshSettingsSummary();
        SpecPathBox.Text = _spec.Output.SpecPath;
        CensusQuestBox.Text = "Manual template. No quest source payload was fetched.";
        CensusGiverBox.Text = "Manual template. No questgiver source payload was fetched.";

        RebuildSections();
        SetWorkflowEnabled(true);
        SelectSection(0);
        RefreshPreview();
        AppendLog($"Created manual template '{QuestTemplateFactory.DisplayName(choice.Kind)}' for '{questName}'.");
    }

    private async Task LoadSpecPreviewAsync()
    {
        var spec = await QuestWorkflow.ReadSpecAsync(RequiredSpecPath());
        ClearLoadedQuestState();
        _spec = spec;

        QuestNameBox.Text = _spec.Quest.Name;
        AuthorBox.Text = _spec.Quest.Author;
        _settings = _settings with { ContentRoot = _spec.Output.ContentRoot, GenerationMode = _spec.GenerationMode };
        RefreshSettingsSummary();
        SpecPathBox.Text = _spec.Output.SpecPath;
        CensusQuestBox.Text = "Loaded from spec. Quest source payload was not fetched in this session.";
        CensusGiverBox.Text = "Loaded from spec. Questgiver source payload was not fetched in this session.";

        RebuildSections();
        SetWorkflowEnabled(true);
        SelectSection(0);
        RefreshPreview();
        AppendLog($"Loaded spec: {_spec.Output.SpecPath}");
    }

    private async Task GenerateFilesAsync()
    {
        if (_spec is null)
            return;

        SaveCurrentSection();
        ApplySettingsGenerationModeToSpec();
        var diagnostics = QuestSpecValidator.Validate(_spec, OverwriteBox.IsChecked == true);
        RefreshDiagnostics(diagnostics);
        var blockers = diagnostics.Where(diagnostic => diagnostic.Severity == QuestDiagnosticSeverity.Blocker).ToArray();
        if (blockers.Length > 0 && AcknowledgeDiagnosticsBox.IsChecked != true)
        {
            MainTabs.SelectedIndex = 1;
            SelectSectionByKey(blockers[0].SectionKey);
            AppendLog("Generation blocked by diagnostics. First blocker: " + blockers[0].Message);
            SetStatus("Diagnostics review required.");
            return;
        }

        var unverified = _sections.Where(section => !_verifiedSections.Contains(section.Key)).ToArray();
        if (unverified.Length > 0)
        {
            SelectSection(_sections.FindIndex(section => section.Key == unverified[0].Key));
            AppendLog("Generation requires every section to be verified. First unverified section: " + unverified[0].Label);
            SetStatus("Verification required.");
            return;
        }

        AppendLog("Generating quest Lua, spawn-starter example, SQL, spec, and missing-data report from verified UI values.");
        var result = await _workflow.GenerateFromSpecAsync(
            _spec,
            OverwriteBox.IsChecked == true,
            CancellationToken.None,
            _spec.GenerationMode,
            strictModuleLuaValidation: AcknowledgeDiagnosticsBox.IsChecked != true);
        _spec = result.Spec;
        RefreshPreview();

        AppendLog("Generated files:");
        foreach (var file in result.WrittenFiles)
            AppendLog("  " + file);
    }

    private async Task ResolveCurrentSectionAsync()
    {
        if (_spec is null || _currentSection is null)
            return;

        SaveCurrentSection();
        AppendLog($"Resolving current section: {_currentSection.Label}");

        switch (_currentSection.Kind)
        {
            case ReviewSectionKind.Quest:
                await _workflow.ResolveQuestIdAsync(_spec);
                break;
            case ReviewSectionKind.Giver:
                await _workflow.ResolveGiverAsync(_spec);
                break;
            case ReviewSectionKind.Step:
                await _workflow.ResolveStepAsync(_spec, _currentSection.StageIndex, _currentSection.StepIndex);
                break;
            case ReviewSectionKind.Rewards:
                await _workflow.ResolveRewardsAsync(_spec);
                break;
            case ReviewSectionKind.Stage:
            case ReviewSectionKind.Output:
                AppendLog("This section has no DB reference to resolve.");
                return;
        }

        _verifiedSections.Remove(_currentSection.Key);
        var index = SectionList.SelectedIndex;
        LoadSection(_currentSection);
        RefreshSectionList(index);
        RefreshPreview();
        AppendLog("Section resolution complete. Review candidates or resolved values before verifying.");
    }

    private async Task OpenSettingsAsync()
    {
        var dialog = new SettingsWindow(_settings);
        var settings = await dialog.ShowDialog<QuestParserUiSettings?>(this);
        if (settings is null)
            return;

        _settings = settings.Normalize();
        await _settings.SaveAsync();
        _workflow = CreateWorkflowFromSettings();
        ApplySettingsGenerationModeToSpec();
        RefreshSettingsSummary();
        ApplyUiSettings();
        RefreshPreview();
        RefreshDiagnostics();
        AppendLog("Settings updated.");
    }

    private async Task OpenVisualEditorAsync()
    {
        if (_spec is null)
        {
            SetStatus("Load, import, or create a quest before opening the visual editor.");
            AppendLog("Visual editor requires a loaded quest spec.");
            return;
        }

        SaveCurrentSection();
        ApplySettingsGenerationModeToSpec();

        var editor = new VisualEditorWindow(_workflow, _spec, ownsSpec: false);
        var result = await editor.ShowDialog<QuestSpec?>(this);
        if (result is null)
            return;

        _spec = result;
        QuestNameBox.Text = _spec.Quest.Name;
        AuthorBox.Text = _spec.Quest.Author;
        SpecPathBox.Text = _spec.Output.SpecPath;
        RebuildSections();
        SetWorkflowEnabled(true);
        SelectSection(0);
        RefreshPreview();
        AppendLog("Visual editor changes returned to the QuestParser review window.");
    }

    private QuestWorkflow CreateWorkflowFromSettings()
    {
        var settings = _settings.Normalize();
        return new QuestWorkflow(
            censusClient: CensusClientFactory.Create(settings.ToCensusOptions()),
            resolver: settings.CreateDatabaseResolver());
    }

    private void RefreshSettingsSummary()
    {
        SettingsSummaryText.Text = _settings.Summary();
        DbConfigText.Text = _settings.DatabaseSummary();
    }

    private void ApplySettingsGenerationModeToSpec()
    {
        if (_spec is not null)
            _spec.GenerationMode = _settings.Normalize().GenerationMode;
    }

    private void ApplyUiSettings()
    {
        var settings = _settings.Normalize();

        FontSize = settings.TextSize;
        MainTabs.FontSize = settings.TabTextSize;
        SectionDetailsTabs.FontSize = settings.TabTextSize;
        GeneratedTabs.FontSize = settings.TabTextSize;
        SectionTitleText.FontSize = settings.SectionTitleTextSize;
        ApplyDataTextSize(settings.DataTextSize);

        QuestSourceGroup.IsVisible = settings.ShowQuestSourcePanel;
        SettingsSummaryStrip.IsVisible = settings.ShowSettingsSummary;
        VerificationGroup.IsVisible = settings.ShowVerificationSteps;
        VerificationSplitter.IsVisible = settings.ShowVerificationSteps;
        WorkGrid.ColumnDefinitions[0].Width = settings.ShowVerificationSteps
            ? new GridLength(settings.SidebarWidth)
            : new GridLength(0);
        WorkGrid.ColumnDefinitions[1].Width = settings.ShowVerificationSteps
            ? new GridLength(8)
            : new GridLength(0);

        SourceDataGroup.IsVisible = settings.ShowSourceDataPanel;
        SourceDataGroup.Height = settings.SourcePanelHeight;
        SectionDetailsTabs.IsVisible = settings.ShowCandidatePanel;
        SectionDetailsTabs.Height = settings.DetailsPanelHeight;
        ProgressGroup.IsVisible = settings.ShowProgressPanel;
    }

    private void ApplyDataTextSize(double dataTextSize)
    {
        foreach (var control in new TemplatedControl[]
        {
            SourceList,
            CandidateList,
            DiagnosticsList,
            SectionLuaBox,
            MissingSpawnBox,
            LuaPreviewBox,
            SpawnScriptPreviewBox,
            SqlPreviewBox,
            MissingPreviewBox,
            SpecPreviewBox,
            CensusQuestBox,
            CensusGiverBox,
            LogBox
        })
        {
            control.FontSize = dataTextSize;
        }
    }

    private async Task LoadRawCensusTabsAsync(string questName)
    {
        var cacheDirectory = _settings.Normalize().CensusCacheDirectory;
        CensusQuestBox.Text = await ReadIfExistsAsync(Path.Combine(cacheDirectory, CensusClient.QuestJsonFileName(questName)));
        CensusGiverBox.Text = await ReadIfExistsAsync(Path.Combine(cacheDirectory, CensusClient.QuestGiverJsonFileName(questName)));
    }

    private static async Task<string> ReadIfExistsAsync(string path)
    {
        return File.Exists(path)
            ? await File.ReadAllTextAsync(path)
            : $"No cached file found at {path}";
    }

    private void ClearLoadedQuestState()
    {
        var wasLoading = _loadingSection;
        var wasRefreshing = _refreshingSectionList;
        _loadingSection = true;
        _refreshingSectionList = true;

        try
        {
            _spec = null;
            _currentSection = null;
            _sections.Clear();
            _verifiedSections.Clear();
            _dirtyEditorKeys.Clear();
            _editors.Clear();

            _sectionRows.Clear();
            _sourceRows.Clear();
            _candidateRows.Clear();
            _diagnosticRows.Clear();
            SectionList.SelectedIndex = -1;
            SectionTitleText.Text = "";
            SectionHelpText.Text = "";
            EditorHost.Children.Clear();
            SectionLuaBox.Text = "";
            LuaPreviewBox.Text = "";
            SpawnScriptPreviewBox.Text = "";
            SqlPreviewBox.Text = "";
            MissingPreviewBox.Text = "";
            SpecPreviewBox.Text = "";
            CensusQuestBox.Text = "";
            CensusGiverBox.Text = "";
            MissingSpawnBox.Text = "Missing NPC guidance appears here when the current DB reference is a missing NPC.";
            AcknowledgeDiagnosticsBox.IsChecked = false;

            SetWorkflowEnabled(false);
            UpdateProgressText();
        }
        finally
        {
            _loadingSection = wasLoading;
            _refreshingSectionList = wasRefreshing;
        }
    }

    private void RebuildSections()
    {
        _sections.Clear();
        if (_spec is null)
            return;

        _sections.Add(new ReviewSection("quest", "1. Quest metadata and DB quest ID", ReviewSectionKind.Quest));
        _sections.Add(new ReviewSection("giver", "2. Quest giver DB reference", ReviewSectionKind.Giver));

        var index = 3;
        for (var stageIndex = 0; stageIndex < _spec.Stages.Count; stageIndex++)
        {
            var stage = _spec.Stages[stageIndex];
            _sections.Add(new ReviewSection($"stage:{stage.Number}", $"{index++}. Stage {stage.Number} text and flow", ReviewSectionKind.Stage, stageIndex));

            for (var stepIndex = 0; stepIndex < stage.Steps.Count; stepIndex++)
            {
                var step = stage.Steps[stepIndex];
                _sections.Add(new ReviewSection($"step:{step.Number}", $"{index++}. Step {step.Number}: {TrimForList(step.Description)}", ReviewSectionKind.Step, stageIndex, stepIndex));
            }
        }

        _sections.Add(new ReviewSection("rewards", $"{index++}. Quest rewards", ReviewSectionKind.Rewards));
        _sections.Add(new ReviewSection("output", $"{index}. Output files and final generated content", ReviewSectionKind.Output));
        RefreshSectionList(0);
    }

    private void SelectSection(int index)
    {
        if (_spec is null || index < 0 || index >= _sections.Count)
            return;

        _selectingSection = true;
        try
        {
            if (!_loadingSection)
                SaveCurrentSection();

            SetSelectedSectionIndex(index);
            LoadSection(_sections[index]);
        }
        finally
        {
            _selectingSection = false;
        }
    }

    private void SelectSectionFromSidebar(int index)
    {
        try
        {
            SelectSection(index);
        }
        catch (Exception ex)
        {
            SetStatus("Section selection failed.");
            AppendLog(ex.ToString());
            SetSelectedSectionIndex(CurrentSectionIndex());
        }
    }

    private void SelectSectionByKey(string sectionKey)
    {
        var normalized = NormalizeDiagnosticSectionKey(sectionKey);
        var index = _sections.FindIndex(section => string.Equals(section.Key, normalized, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            SelectSection(index);
    }

    private static string NormalizeDiagnosticSectionKey(string sectionKey)
    {
        var optionIndex = sectionKey.IndexOf(".option:", StringComparison.OrdinalIgnoreCase);
        return optionIndex >= 0 ? sectionKey[..optionIndex] : sectionKey;
    }

    private void RefreshSectionList(int selectedIndex)
    {
        var wasRefreshing = _refreshingSectionList;
        _refreshingSectionList = true;
        try
        {
            _sectionRows.Clear();
            foreach (var section in _sections)
                _sectionRows.Add(new SectionDisplay(_verifiedSections.Contains(section.Key), section.Label));

            SetSelectedSectionIndex(selectedIndex);
        }
        finally
        {
            _refreshingSectionList = wasRefreshing;
        }
        UpdateProgressText();
        RefreshActionStates();
    }

    private void SetSelectedSectionIndex(int selectedIndex)
    {
        var boundedIndex = selectedIndex >= 0 && selectedIndex < _sections.Count ? selectedIndex : -1;
        if (SectionList.SelectedIndex == boundedIndex)
            return;

        var wasRefreshing = _refreshingSectionList;
        _refreshingSectionList = true;
        try
        {
            SectionList.SelectedIndex = boundedIndex;
        }
        finally
        {
            _refreshingSectionList = wasRefreshing;
        }
    }

    private int CurrentSectionIndex()
    {
        if (_currentSection is null)
            return -1;

        return _sections.FindIndex(section => section.Equals(_currentSection));
    }

    private void LoadSection(ReviewSection section)
    {
        if (_spec is null)
            return;

        _loadingSection = true;
        try
        {
            _currentSection = section;
            _editors.Clear();
            _sourceRows.Clear();
            _candidateRows.Clear();
            EditorHost.Children.Clear();
            MissingSpawnBox.Text = "Missing NPC guidance appears here when the current DB reference is a missing NPC.";

            SectionTitleText.Text = section.Label;
            SectionHelpText.Text = "Review the source values, DB resolution, generated Lua, and editable spec fields. Verify the section after the values are correct.";

            switch (section.Kind)
            {
                case ReviewSectionKind.Quest:
                    LoadQuestSection();
                    break;
                case ReviewSectionKind.Giver:
                    LoadGiverSection();
                    break;
                case ReviewSectionKind.Stage:
                    LoadStageSection(section);
                    break;
                case ReviewSectionKind.Step:
                    LoadStepSection(section);
                    break;
                case ReviewSectionKind.Rewards:
                    LoadRewardsSection();
                    break;
                case ReviewSectionKind.Output:
                    LoadOutputSection();
                    break;
            }

            LoadCandidatesForCurrentSection();
        }
        finally
        {
            _loadingSection = false;
        }
        RefreshPreview();
        RefreshActionStates();
    }

    private void LoadQuestSection()
    {
        var spec = RequireSpec();
        SetSourceRows(
            ("Census id", spec.Quest.CensusId),
            ("Census CRC", spec.Quest.CensusCrc),
            ("Name", spec.Quest.Name),
            ("Zone/category", spec.Quest.Zone),
            ("Level", spec.Quest.Level),
            ("Tier", spec.Quest.Tier),
            ("Repeatable", spec.Quest.Repeatable),
            ("Tradeskill", spec.Quest.IsTradeskill),
            ("Starter text", spec.Quest.StarterText),
            ("Completion text", spec.Quest.CompletionText),
            ("DB quest status", spec.QuestId.Status),
            ("DB quest id", spec.QuestId.Id?.ToString(CultureInfo.InvariantCulture) ?? ""));

        AddHeader("Quest metadata");
        AddTextEditor("quest.name", "Quest name", spec.Quest.Name);
        AddTextEditor("quest.zone", "Zone/category", spec.Quest.Zone);
        AddNumericEditor("quest.level", "Level", spec.Quest.Level, 0, 255);
        AddNumericEditor("quest.tier", "Tier", spec.Quest.Tier, 0, 255);
        AddTextEditor("quest.author", "Author", spec.Quest.Author);
        AddBoolEditor("quest.repeatable", "Repeatable", spec.Quest.Repeatable);
        AddBoolEditor("quest.shareable", "Shareable", spec.Quest.Shareable);
        AddBoolEditor("quest.completeShareable", "Complete shareable", spec.Quest.CompleteShareable);
        AddBoolEditor("quest.tradeskill", "Tradeskill", spec.Quest.IsTradeskill);
        AddBoolEditor("quest.scales", "Scales with level", spec.Quest.ScalesWithLevel);
        AddTextEditor("quest.starter", "Starter text", spec.Quest.StarterText, multiline: true);
        AddTextEditor("quest.completion", "Completion text", spec.Quest.CompletionText, multiline: true);
        AddHeader("Database quest ID");
        AddReferenceEditor("questId", spec.QuestId);
    }

    private void LoadGiverSection()
    {
        var spec = RequireSpec();
        SetSourceRows(
            ("Census questgivers", string.Join(", ", spec.QuestGivers)),
            ("Selected query", spec.Giver.Query),
            ("DB status", spec.Giver.Status),
            ("DB id", spec.Giver.Id?.ToString(CultureInfo.InvariantCulture) ?? ""),
            ("DB name", spec.Giver.Name),
            ("Candidates", spec.Giver.Candidates.Count));

        AddHeader("Quest giver");
        AddTextEditor("giver.list", "Census questgivers", string.Join(", ", spec.QuestGivers), multiline: true);
        AddReferenceEditor("giver", spec.Giver);
    }

    private void LoadStageSection(ReviewSection section)
    {
        var stage = GetStage(section);
        SetSourceRows(
            ("Stage number", stage.Number),
            ("Starter text", stage.Description),
            ("Completion text", stage.CompletedDescription),
            ("Parallel steps", stage.IsParallel),
            ("Step count", stage.Steps.Count));

        AddHeader($"Stage {stage.Number}");
        AddTextEditor("stage.description", "Task group text", stage.Description, multiline: true);
        AddTextEditor("stage.completed", "Completed group text", stage.CompletedDescription, multiline: true);
        AddBoolEditor("stage.parallel", "Parallel stage", stage.IsParallel);
    }

    private void LoadStepSection(ReviewSection section)
    {
        var step = GetStep(section);
        SetSourceRows(
            ("Step number", step.Number),
            ("Inferred Lua function", step.Type),
            ("Description", step.Description),
            ("Completed text", step.CompletedDescription),
            ("Quantity min", step.QuantityMin),
            ("Quantity max", step.QuantityMax),
            ("Percentage", step.Percentage),
            ("Icon id", step.IconId),
            ("Icon name", step.IconName),
            ("Completion zone", step.CompletionZone),
            ("Search text", step.SearchText),
            ("Random options", step.RandomOptions.Count),
            ("Target status", step.Target.Status),
            ("Target id", step.Target.Id?.ToString(CultureInfo.InvariantCulture) ?? ""),
            ("Target name", step.Target.Name),
            ("Candidates", step.Target.Candidates.Count));

        AddHeader($"Step {step.Number}");
        AddComboEditor("step.type", "Lua step function", step.Type);
        AddTextEditor("step.description", "Description", step.Description, multiline: true);
        AddTextEditor("step.completed", "Completed text", step.CompletedDescription, multiline: true);
        AddNumericEditor("step.quantityMin", "Quantity min", step.QuantityMin, 0, 100000);
        AddNumericEditor("step.quantityMax", "Quantity max", step.QuantityMax, 0, 100000);
        AddNumericEditor("step.percentage", "Percentage", Convert.ToDecimal(step.Percentage, CultureInfo.InvariantCulture), 0, 100, 2);
        AddNumericEditor("step.iconId", "Icon id", step.IconId, 0, 1000000);
        AddTextEditor("step.iconName", "Icon name", step.IconName);
        AddTextEditor("step.completionZone", "Completion zone", step.CompletionZone);
        AddTextEditor("step.searchText", "DB search text", step.SearchText);

        if (step.HasRandomOptions)
        {
            AddHeader("Random selection options");
            for (var i = 0; i < step.RandomOptions.Count; i++)
            {
                var option = step.RandomOptions[i];
                AddReadOnlyText(
                    $"Option {i + 1}",
                    $"{option.QuantityMin}-{option.QuantityMax} | {option.SearchText} | {option.Target.Status} | {option.Target.Id?.ToString(CultureInfo.InvariantCulture) ?? "no id"} | {option.Target.Name}");
            }
        }
        else
        {
            AddHeader("Step target DB reference");
            AddReferenceEditor("stepTarget", step.Target);
        }

        if (step.Type is StepType.Location or StepType.ZoneLocation)
        {
            step.Location ??= new LocationTarget();
            AddHeader("Location");
            AddNumericEditor("location.x", "X", Convert.ToDecimal(step.Location.X, CultureInfo.InvariantCulture), -100000, 100000, 3);
            AddNumericEditor("location.y", "Y", Convert.ToDecimal(step.Location.Y, CultureInfo.InvariantCulture), -100000, 100000, 3);
            AddNumericEditor("location.z", "Z", Convert.ToDecimal(step.Location.Z, CultureInfo.InvariantCulture), -100000, 100000, 3);
            AddNumericEditor("location.radius", "Radius", Convert.ToDecimal(step.Location.Radius, CultureInfo.InvariantCulture), 0, 100000, 3);
            AddReferenceEditor("locationZone", step.Location.Zone);
        }
    }

    private void LoadRewardsSection()
    {
        var spec = RequireSpec();
        SetSourceRows(
            ("Coin min", spec.Rewards.CoinMin),
            ("Coin max", spec.Rewards.CoinMax),
            ("Experience", spec.Rewards.Experience),
            ("Reward items", spec.Rewards.Items.Count),
            ("Reward factions", spec.Rewards.Factions.Count));

        AddHeader("Coin and experience");
        AddNumericEditor("rewards.coinMin", "Coin min", spec.Rewards.CoinMin, 0, int.MaxValue);
        AddNumericEditor("rewards.coinMax", "Coin max", spec.Rewards.CoinMax, 0, int.MaxValue);
        AddNumericEditor("rewards.xp", "Experience", Convert.ToDecimal(spec.Rewards.Experience, CultureInfo.InvariantCulture), 0, int.MaxValue, 2);

        AddHeader("Reward items");
        AddButtonRow("Items", "Add Item Reward", AddRewardItem);
        if (spec.Rewards.Items.Count == 0)
        {
            AddReadOnlyText("Items", "No item rewards in this spec.");
        }
        else
        {
            for (var i = 0; i < spec.Rewards.Items.Count; i++)
            {
                var itemIndex = i;
                var reward = spec.Rewards.Items[i];
                AddHeader($"Reward item {i + 1}");
                AddButtonRow("Item actions", "Remove Item Reward", () => RemoveRewardItem(itemIndex));
                AddNumericEditor($"rewardItem.{i}.quantity", "Quantity", reward.Quantity, 0, 100000);
                AddBoolEditor($"rewardItem.{i}.selectable", "Selectable", reward.IsSelectable);
                AddReferenceEditor($"rewardItem.{i}.item", reward.Item);
            }
        }

        AddHeader("Reward factions");
        AddButtonRow("Factions", "Add Faction Reward", AddRewardFaction);
        if (spec.Rewards.Factions.Count == 0)
        {
            AddReadOnlyText("Factions", "No faction rewards in this spec.");
        }
        else
        {
            for (var i = 0; i < spec.Rewards.Factions.Count; i++)
            {
                var factionIndex = i;
                var reward = spec.Rewards.Factions[i];
                AddHeader($"Reward faction {i + 1}");
                AddButtonRow("Faction actions", "Remove Faction Reward", () => RemoveRewardFaction(factionIndex));
                AddNumericEditor($"rewardFaction.{i}.amount", "Amount", reward.Amount, int.MinValue, int.MaxValue);
                AddReferenceEditor($"rewardFaction.{i}.faction", reward.Faction);
            }
        }
    }

    private void LoadOutputSection()
    {
        var spec = RequireSpec();
        SetSourceRows(
            ("Content root", spec.Output.ContentRoot),
            ("Quest directory", spec.Output.QuestDirectory),
            ("Lua path", spec.Output.LuaPath),
            ("Spawn script example path", spec.Output.SpawnScriptPath),
            ("Spec path", spec.Output.SpecPath),
            ("SQL path", spec.Output.SqlPath),
            ("Missing report path", spec.Output.MissingReportPath),
            ("Runtime preview path", spec.Output.PreviewPath),
            ("Lua written", spec.Generation.LuaWritten),
            ("Spawn script written", spec.Generation.SpawnScriptWritten),
            ("Spec written", spec.Generation.SpecWritten),
            ("SQL written", spec.Generation.SqlWritten),
            ("Missing report written", spec.Generation.MissingReportWritten));

        AddHeader("Output paths");
        AddTextEditor("output.contentRoot", "Content root", spec.Output.ContentRoot);
        AddTextEditor("output.questDirectory", "Quest directory", spec.Output.QuestDirectory);
        AddTextEditor("output.lua", "Lua path", spec.Output.LuaPath);
        AddTextEditor("output.spawnScript", "Spawn script example path", spec.Output.SpawnScriptPath);
        AddTextEditor("output.spec", "Spec JSON path", spec.Output.SpecPath);
        AddTextEditor("output.sql", "SQL path", spec.Output.SqlPath);
        AddTextEditor("output.missing", "Missing report path", spec.Output.MissingReportPath);
        AddTextEditor("output.preview", "Runtime preview path", spec.Output.PreviewPath);
    }

    private void SaveCurrentSection()
    {
        if (_loadingSection || _spec is null || _currentSection is null)
            return;

        switch (_currentSection.Kind)
        {
            case ReviewSectionKind.Quest:
                SaveQuestSection();
                break;
            case ReviewSectionKind.Giver:
                SaveGiverSection();
                break;
            case ReviewSectionKind.Stage:
                SaveStageSection(_currentSection);
                break;
            case ReviewSectionKind.Step:
                SaveStepSection(_currentSection);
                break;
            case ReviewSectionKind.Rewards:
                SaveRewardsSection();
                break;
            case ReviewSectionKind.Output:
                SaveOutputSection();
                break;
        }
    }

    private void SaveQuestSection()
    {
        var spec = RequireSpec();
        spec.Quest.Name = ReadText("quest.name");
        spec.Quest.Zone = ReadText("quest.zone");
        spec.Quest.Level = (byte)Math.Clamp(ReadInt("quest.level"), 0, 255);
        spec.Quest.Tier = (byte)Math.Clamp(ReadInt("quest.tier"), 0, 255);
        spec.Quest.Author = ReadText("quest.author");
        spec.Quest.Repeatable = ReadBool("quest.repeatable");
        spec.Quest.Shareable = ReadBool("quest.shareable");
        spec.Quest.CompleteShareable = ReadBool("quest.completeShareable");
        spec.Quest.IsTradeskill = ReadBool("quest.tradeskill");
        spec.Quest.ScalesWithLevel = ReadBool("quest.scales");
        spec.Quest.StarterText = ReadText("quest.starter");
        spec.Quest.CompletionText = ReadText("quest.completion");
        SaveReference("questId", spec.QuestId);
    }

    private void SaveGiverSection()
    {
        var spec = RequireSpec();
        spec.QuestGivers = ReadText("giver.list")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        SaveReference("giver", spec.Giver);
    }

    private void SaveStageSection(ReviewSection section)
    {
        var stage = GetStage(section);
        stage.Description = ReadText("stage.description");
        stage.CompletedDescription = ReadText("stage.completed");
        stage.IsParallel = ReadBool("stage.parallel");
    }

    private void SaveStepSection(ReviewSection section)
    {
        var step = GetStep(section);
        step.Type = ReadEnum("step.type", step.Type);
        step.Description = ReadText("step.description");
        step.CompletedDescription = ReadText("step.completed");
        step.QuantityMin = ReadInt("step.quantityMin");
        step.QuantityMax = Math.Max(1, ReadInt("step.quantityMax"));
        step.Percentage = Convert.ToSingle(ReadDecimal("step.percentage"), CultureInfo.InvariantCulture);
        step.IconId = ReadInt("step.iconId");
        step.IconName = ReadText("step.iconName");
        step.CompletionZone = ReadText("step.completionZone");
        step.SearchText = ReadText("step.searchText");

        if (!step.HasRandomOptions)
            SaveReference("stepTarget", step.Target);

        if (_editors.ContainsKey("location.x"))
        {
            step.Location ??= new LocationTarget();
            step.Location.X = Convert.ToSingle(ReadDecimal("location.x"), CultureInfo.InvariantCulture);
            step.Location.Y = Convert.ToSingle(ReadDecimal("location.y"), CultureInfo.InvariantCulture);
            step.Location.Z = Convert.ToSingle(ReadDecimal("location.z"), CultureInfo.InvariantCulture);
            step.Location.Radius = Convert.ToSingle(ReadDecimal("location.radius"), CultureInfo.InvariantCulture);
            SaveReference("locationZone", step.Location.Zone);
        }
    }

    private void SaveRewardsSection()
    {
        var spec = RequireSpec();
        spec.Rewards.CoinMin = ReadInt("rewards.coinMin");
        spec.Rewards.CoinMax = ReadInt("rewards.coinMax");
        spec.Rewards.Experience = Convert.ToDouble(ReadDecimal("rewards.xp"), CultureInfo.InvariantCulture);

        for (var i = 0; i < spec.Rewards.Items.Count; i++)
        {
            var reward = spec.Rewards.Items[i];
            reward.Quantity = Math.Max(1, ReadInt($"rewardItem.{i}.quantity"));
            reward.IsSelectable = ReadBool($"rewardItem.{i}.selectable");
            SaveReference($"rewardItem.{i}.item", reward.Item);
        }

        for (var i = 0; i < spec.Rewards.Factions.Count; i++)
        {
            var reward = spec.Rewards.Factions[i];
            reward.Amount = ReadInt($"rewardFaction.{i}.amount");
            SaveReference($"rewardFaction.{i}.faction", reward.Faction);
        }
    }

    private void SaveOutputSection()
    {
        var spec = RequireSpec();
        spec.Output.ContentRoot = ReadText("output.contentRoot");
        spec.Output.QuestDirectory = ReadText("output.questDirectory");
        spec.Output.LuaPath = ReadText("output.lua");
        spec.Output.SpawnScriptPath = ReadText("output.spawnScript");
        spec.Output.SpecPath = ReadText("output.spec");
        spec.Output.SqlPath = ReadText("output.sql");
        spec.Output.MissingReportPath = ReadText("output.missing");
        spec.Output.PreviewPath = ReadText("output.preview");
        _settings = _settings with { ContentRoot = spec.Output.ContentRoot };
        RefreshSettingsSummary();
        SpecPathBox.Text = spec.Output.SpecPath;
    }

    private void VerifyCurrentSection()
    {
        if (_currentSection is null)
            return;

        SaveCurrentSection();
        _verifiedSections.Add(_currentSection.Key);
        var index = _sections.FindIndex(section => section.Key == _currentSection.Key);
        RefreshSectionList(index);
        RefreshPreview();
        AppendLog($"Verified: {_currentSection.Label}");
    }

    private void MoveSection(int delta)
    {
        if (SectionList.SelectedIndex < 0)
            return;

        SelectSection(Math.Clamp(SectionList.SelectedIndex + delta, 0, _sections.Count - 1));
    }

    private void AddRewardItem()
    {
        var spec = RequireSpec();
        SaveCurrentSection();
        spec.Rewards.Items.Add(new RewardItemSpec
        {
            Quantity = 1,
            Item = ResolvedReference.Missing("item", "")
        });
        ReloadCurrentSectionAfterStructuralEdit("rewards.items");
    }

    private void RemoveRewardItem(int index)
    {
        var spec = RequireSpec();
        SaveCurrentSection();
        if (index >= 0 && index < spec.Rewards.Items.Count)
            spec.Rewards.Items.RemoveAt(index);
        ReloadCurrentSectionAfterStructuralEdit("rewards.items");
    }

    private void AddRewardFaction()
    {
        var spec = RequireSpec();
        SaveCurrentSection();
        spec.Rewards.Factions.Add(new RewardFactionSpec
        {
            Faction = ResolvedReference.Missing("faction", "")
        });
        ReloadCurrentSectionAfterStructuralEdit("rewards.factions");
    }

    private void RemoveRewardFaction(int index)
    {
        var spec = RequireSpec();
        SaveCurrentSection();
        if (index >= 0 && index < spec.Rewards.Factions.Count)
            spec.Rewards.Factions.RemoveAt(index);
        ReloadCurrentSectionAfterStructuralEdit("rewards.factions");
    }

    private void ReloadCurrentSectionAfterStructuralEdit(string provenanceKey)
    {
        if (_currentSection is null)
            return;

        MarkProvenance(provenanceKey, "User override");
        _verifiedSections.Remove(_currentSection.Key);
        AcknowledgeDiagnosticsBox.IsChecked = false;
        var index = SectionList.SelectedIndex;
        LoadSection(_currentSection);
        RefreshSectionList(index);
        RefreshPreview();
    }

    private void EditorChanged(string? key = null)
    {
        if (_loadingSection || _spec is null || _currentSection is null)
            return;

        if (!string.IsNullOrWhiteSpace(key))
        {
            _dirtyEditorKeys.Add(key);
            MarkProvenance(EditorKeyToProvenanceKey(key), "User override");
        }

        SaveCurrentSection();
        _verifiedSections.Remove(_currentSection.Key);
        AcknowledgeDiagnosticsBox.IsChecked = false;
        var index = _sections.FindIndex(section => section.Key == _currentSection.Key);
        RefreshSectionList(index);
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (_spec is null)
        {
            LuaPreviewBox.Text = "";
            SpawnScriptPreviewBox.Text = "";
            SqlPreviewBox.Text = "";
            MissingPreviewBox.Text = "";
            SpecPreviewBox.Text = "";
            SectionLuaBox.Text = "";
            _diagnosticRows.Clear();
            return;
        }

        try
        {
            var preview = _workflow.Preview(_spec);
            LuaPreviewBox.Text = preview.Lua;
            SpawnScriptPreviewBox.Text = preview.SpawnScript;
            SqlPreviewBox.Text = preview.Sql;
            MissingPreviewBox.Text = preview.MissingReport;
            SpecPreviewBox.Text = JsonSerializer.Serialize(_spec, QuestSpecJsonContext.Default.QuestSpec);
            SectionLuaBox.Text = BuildSectionLuaPreview(preview.Lua);
            RefreshDiagnostics();
        }
        catch (Exception ex)
        {
            SectionLuaBox.Text = ex.Message;
        }
    }

    private void RefreshDiagnostics(List<QuestDiagnostic>? diagnostics = null)
    {
        _diagnosticRows.Clear();
        if (_spec is null)
            return;

        diagnostics ??= QuestSpecValidator.Validate(_spec, OverwriteBox.IsChecked == true);
        if (diagnostics.Count == 0)
        {
            _diagnosticRows.Add("No diagnostics.");
            return;
        }

        foreach (var diagnostic in diagnostics)
            _diagnosticRows.Add($"{diagnostic.Severity,-7} {diagnostic.SectionKey,-18} {diagnostic.Code,-24} {diagnostic.Message}");
    }

    private string BuildSectionLuaPreview(string lua)
    {
        if (_currentSection is null)
            return lua;

        if (_currentSection.Kind == ReviewSectionKind.Step)
        {
            var step = GetStep(_currentSection);
            return ExtractFunctions(lua, $"function AddStage{GetStage(_currentSection).Number}Steps", $"function Step{step.Number}Complete");
        }

        if (_currentSection.Kind == ReviewSectionKind.Stage)
        {
            var stage = GetStage(_currentSection);
            return ExtractFunctions(lua, $"function AddStage{stage.Number}Steps", $"function CheckProgressStage{stage.Number}");
        }

        if (_currentSection.Kind is ReviewSectionKind.Quest or ReviewSectionKind.Giver)
            return string.Join(Environment.NewLine, lua.ReplaceLineEndings("\n").Split('\n').Take(36));

        return lua;
    }

    private static string ExtractFunctions(string lua, params string[] functionPrefixes)
    {
        var chunks = new List<string>();
        foreach (var prefix in functionPrefixes)
        {
            var start = lua.IndexOf(prefix, StringComparison.Ordinal);
            if (start < 0)
                continue;
            var next = lua.IndexOf("\nfunction ", start + prefix.Length, StringComparison.Ordinal);
            chunks.Add(next < 0 ? lua[start..].TrimEnd() : lua[start..next].TrimEnd());
        }

        return chunks.Count == 0 ? lua : string.Join(Environment.NewLine + Environment.NewLine, chunks);
    }

    private void LoadCandidatesForCurrentSection()
    {
        _candidateRows.Clear();
        foreach (var context in CurrentReferenceContexts())
        {
            foreach (var candidate in context.Reference.Candidates)
                _candidateRows.Add(new CandidateDisplay(context.Label, context.OptionIndex, candidate));
        }

        var missingNpcContext = CurrentReferenceContexts().FirstOrDefault(context =>
            string.Equals(context.Reference.Kind, "npc", StringComparison.OrdinalIgnoreCase)
            && context.Reference.Status == ResolveStatus.Missing);
        RefreshMissingSpawnWizard(missingNpcContext?.Reference);

        RefreshActionStates();
    }

    private void RefreshMissingSpawnWizard(ResolvedReference? reference)
    {
        if (_spec is null)
            return;

        if (reference is null
            || !string.Equals(reference.Kind, "npc", StringComparison.OrdinalIgnoreCase)
            || reference.Status != ResolveStatus.Missing)
        {
            MissingSpawnBox.Text = "Missing NPC guidance appears here when the current DB reference is a missing NPC.";
            return;
        }

        var context = _currentSection?.Label ?? "Current section";
        var template = MissingSpawnTemplateBuilder.Build(_spec, reference, context);
        MissingSpawnBox.Text = MissingSpawnTemplateBuilder.Format(template);
    }

    private void UseSelectedCandidate()
    {
        var candidate = CurrentCandidate();
        if (candidate is null || _currentSection is null)
            return;

        var reference = ReferenceForCandidate(candidate);
        if (reference is null)
            return;

        ApplyCandidate(reference, candidate.Candidate);

        if (_currentSection.Kind == ReviewSectionKind.Step && candidate.OptionIndex.HasValue)
        {
            var step = GetStep(_currentSection);
            MarkProvenance($"step.{step.Number}.randomOptions", "User selected DB candidate");
        }
        else if (_currentSection.Kind == ReviewSectionKind.Step)
        {
            var step = GetStep(_currentSection);
            step.Target = reference;
            MarkProvenance($"step.{step.Number}.target", "User selected DB candidate");
        }
        else if (_currentSection.Kind == ReviewSectionKind.Quest)
        {
            RequireSpec().QuestId = reference;
            MarkProvenance("questId", "User selected DB candidate");
        }
        else if (_currentSection.Kind == ReviewSectionKind.Giver)
        {
            RequireSpec().Giver = reference;
            MarkProvenance("giver", "User selected DB candidate");
        }

        _verifiedSections.Remove(_currentSection.Key);
        var index = SectionList.SelectedIndex;
        LoadSection(_currentSection);
        RefreshSectionList(index);
        AppendLog($"Selected candidate {candidate.Candidate.Id}: {candidate.Candidate.Name}");
    }

    private static void ApplyCandidate(ResolvedReference reference, ResolveCandidate candidate)
    {
        reference.Status = ResolveStatus.Resolved;
        reference.Id = candidate.Id;
        reference.Ids = [candidate.Id];
        reference.Name = candidate.Name;
        reference.Kind = string.IsNullOrWhiteSpace(candidate.Kind) ? reference.Kind : candidate.Kind;
        reference.Source = "User selected DB candidate";
        reference.Metadata = new Dictionary<string, string>(candidate.Metadata, StringComparer.OrdinalIgnoreCase);
        reference.Candidates.Clear();
    }

    private CandidateDisplay? CurrentCandidate()
    {
        return CandidateList.SelectedItem as CandidateDisplay;
    }

    private ResolvedReference? ReferenceForCandidate(CandidateDisplay candidate)
    {
        if (_spec is null || _currentSection is null)
            return null;

        if (_currentSection.Kind == ReviewSectionKind.Step)
        {
            var step = GetStep(_currentSection);
            if (candidate.OptionIndex is int optionIndex && optionIndex >= 0 && optionIndex < step.RandomOptions.Count)
                return step.RandomOptions[optionIndex].Target;
            return step.Target;
        }

        return _currentSection.Kind switch
        {
            ReviewSectionKind.Quest => _spec.QuestId,
            ReviewSectionKind.Giver => _spec.Giver,
            _ => null
        };
    }

    private IEnumerable<ReferenceContext> CurrentReferenceContexts()
    {
        if (_spec is null || _currentSection is null)
            yield break;

        switch (_currentSection.Kind)
        {
            case ReviewSectionKind.Quest:
                yield return new ReferenceContext("Quest ID", null, _spec.QuestId);
                break;
            case ReviewSectionKind.Giver:
                yield return new ReferenceContext("Quest giver", null, _spec.Giver);
                break;
            case ReviewSectionKind.Step:
                var step = GetStep(_currentSection);
                if (step.HasRandomOptions)
                {
                    for (var i = 0; i < step.RandomOptions.Count; i++)
                    {
                        var option = step.RandomOptions[i];
                        yield return new ReferenceContext($"Option {i + 1}: {TrimForList(option.SearchText)}", i, option.Target);
                    }
                }
                else
                {
                    yield return new ReferenceContext("Step target", null, step.Target);
                }
                break;
        }
    }

    private void SaveReference(string prefix, ResolvedReference reference)
    {
        if (!_editors.ContainsKey(prefix + ".kind"))
            return;

        reference.Kind = ReadText(prefix + ".kind");
        reference.Query = ReadText(prefix + ".query");
        reference.Status = ReadEnum(prefix + ".status", reference.Status);
        reference.Id = ReadNullableInt(prefix + ".id");
        reference.Name = ReadText(prefix + ".name");
        reference.Ids = reference.Id.HasValue ? [reference.Id.Value] : [];
        var source = ReadText(prefix + ".source");
        if (!string.IsNullOrWhiteSpace(source))
            reference.Source = source;
        if (_dirtyEditorKeys.Any(key => key.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase)))
            reference.Source = "User override";
        if (reference.Status is ResolveStatus.Resolved or ResolveStatus.Proposed)
            reference.Candidates.Clear();
    }

    private void AddReferenceEditor(string prefix, ResolvedReference reference)
    {
        AddTextEditor(prefix + ".kind", "Kind", reference.Kind);
        AddTextEditor(prefix + ".query", "Query/search text", reference.Query);
        AddComboEditor(prefix + ".status", "Status", reference.Status);
        AddTextEditor(prefix + ".id", "ID", reference.Id?.ToString(CultureInfo.InvariantCulture) ?? "");
        AddTextEditor(prefix + ".name", "Resolved name", reference.Name);
        AddTextEditor(prefix + ".source", "Source", reference.Source);
    }

    private void AddHeader(string text)
    {
        EditorHost.Children.Add(new TextBlock
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            Margin = new Avalonia.Thickness(0, 10, 0, 2)
        });
    }

    private void AddTextEditor(string key, string labelText, string value, bool multiline = false)
    {
        var box = new TextBox
        {
            Text = value,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 74 : 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        ScrollViewer.SetVerticalScrollBarVisibility(
            box,
            multiline ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        box.TextChanged += (_, _) => EditorChanged(key);
        AddEditorControl(key, labelText, box);
    }

    private void AddReadOnlyText(string labelText, string value)
    {
        var box = new TextBox
        {
            Text = value,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 44,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        ScrollViewer.SetVerticalScrollBarVisibility(box, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        AddEditorControl(null, labelText, box);
    }

    private void AddBoolEditor(string key, string labelText, bool value)
    {
        var check = new CheckBox
        {
            IsChecked = value,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        check.IsCheckedChanged += (_, _) => EditorChanged(key);
        AddEditorControl(key, labelText, check);
    }

    private void AddComboEditor<T>(string key, string labelText, T value) where T : struct, Enum
    {
        var combo = new ComboBox
        {
            ItemsSource = Enum.GetValues<T>(),
            SelectedItem = value,
            MinWidth = 180,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        combo.SelectionChanged += (_, _) => EditorChanged(key);
        AddEditorControl(key, labelText, combo);
    }

    private void AddNumericEditor(string key, string labelText, decimal value, decimal minimum, decimal maximum, int decimalPlaces = 0)
    {
        var number = new NumericUpDown
        {
            Minimum = minimum,
            Maximum = maximum,
            Increment = decimalPlaces > 0 ? 0.1m : 1m,
            Value = Math.Clamp(value, minimum, maximum),
            FormatString = "F" + decimalPlaces.ToString(CultureInfo.InvariantCulture),
            Width = 170,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        number.ValueChanged += (_, _) => EditorChanged(key);
        AddEditorControl(key, labelText, number);
    }

    private void AddButtonRow(string labelText, string buttonText, Action action)
    {
        var button = new Button
        {
            Content = buttonText,
            MinWidth = 150,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        button.Click += (_, _) => RunSync(buttonText, action);
        AddEditorControl(null, labelText, button);
    }

    private void AddEditorControl(string? key, string labelText, Control control)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("190,*"),
            ColumnSpacing = 8,
            Margin = new Avalonia.Thickness(0, 1, 0, 1)
        };
        row.Children.Add(new TextBlock
        {
            Text = labelText,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        EditorHost.Children.Add(row);

        if (!string.IsNullOrWhiteSpace(key))
            _editors[key] = control;
    }

    private string ReadText(string key)
    {
        return _editors.TryGetValue(key, out var control)
            ? control switch
            {
                TextBox box => (box.Text ?? "").Trim(),
                ComboBox combo => Convert.ToString(combo.SelectedItem, CultureInfo.InvariantCulture) ?? "",
                NumericUpDown number => Convert.ToString(number.Value, CultureInfo.InvariantCulture) ?? "",
                _ => ""
            }
            : "";
    }

    private int ReadInt(string key)
    {
        if (!_editors.TryGetValue(key, out var control))
            return 0;
        if (control is NumericUpDown number)
            return Convert.ToInt32(number.Value ?? 0, CultureInfo.InvariantCulture);
        return int.TryParse(ReadText(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private int? ReadNullableInt(string key)
    {
        var text = ReadText(key);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private decimal ReadDecimal(string key)
    {
        if (!_editors.TryGetValue(key, out var control))
            return 0;
        if (control is NumericUpDown number)
            return number.Value ?? 0;
        return decimal.TryParse(ReadText(key), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private bool ReadBool(string key)
    {
        return _editors.TryGetValue(key, out var control) && control is CheckBox check && check.IsChecked == true;
    }

    private T ReadEnum<T>(string key, T fallback) where T : struct, Enum
    {
        return Enum.TryParse<T>(ReadText(key), out var parsed) ? parsed : fallback;
    }

    private QuestSpec RequireSpec()
    {
        return _spec ?? throw new InvalidOperationException("No quest is loaded.");
    }

    private QuestStageSpec GetStage(ReviewSection section)
    {
        return RequireSpec().Stages[section.StageIndex];
    }

    private QuestStepSpec GetStep(ReviewSection section)
    {
        return RequireSpec().Stages[section.StageIndex].Steps[section.StepIndex];
    }

    private void SetSourceRows(params (string Field, object? Value)[] rows)
    {
        _sourceRows.Clear();
        foreach (var (field, value) in rows)
            _sourceRows.Add($"{field,-22} {FormatSourceValue(value),-44} {SourceForField(field)}");
    }

    private string SourceForField(string field)
    {
        if (_spec is null || _currentSection is null)
            return "";

        if (field.StartsWith("Census", StringComparison.OrdinalIgnoreCase))
            return "Census";
        if (field.StartsWith("DB", StringComparison.OrdinalIgnoreCase) || field.Contains("Candidates", StringComparison.OrdinalIgnoreCase))
            return CurrentReferenceContexts().FirstOrDefault()?.Reference.Source ?? "DB resolver";

        if (_currentSection.Kind == ReviewSectionKind.Quest)
        {
            return field switch
            {
                "Name" => Provenance("quest.name"),
                "Zone/category" => Provenance("quest.zone"),
                "Level" => Provenance("quest.level"),
                "Tier" => Provenance("quest.tier"),
                "Repeatable" => Provenance("quest.repeatable"),
                "Tradeskill" => Provenance("quest.tradeskill"),
                "Starter text" => Provenance("quest.starter"),
                "Completion text" => Provenance("quest.completion"),
                _ => "Census quest"
            };
        }

        if (_currentSection.Kind == ReviewSectionKind.Step)
        {
            var step = GetStep(_currentSection);
            return field switch
            {
                "Inferred Lua function" => Provenance($"step.{step.Number}.type"),
                "Description" => Provenance($"step.{step.Number}.description"),
                "Completed text" => Provenance($"step.{step.Number}.completed"),
                "Quantity min" => Provenance($"step.{step.Number}.quantityMin"),
                "Quantity max" => Provenance($"step.{step.Number}.quantityMax"),
                "Icon id" => Provenance($"step.{step.Number}.iconId"),
                "Icon name" => Provenance($"step.{step.Number}.iconName"),
                "Completion zone" => Provenance($"step.{step.Number}.completionZone"),
                "Search text" => Provenance($"step.{step.Number}.searchText"),
                "Target status" or "Target id" or "Target name" => step.Target.Source,
                _ => "Census/generated"
            };
        }

        if (_currentSection.Kind == ReviewSectionKind.Giver)
            return CurrentReferenceContexts().FirstOrDefault()?.Reference.Source ?? Provenance("giver.query");
        if (_currentSection.Kind == ReviewSectionKind.Rewards)
            return Provenance("rewards." + (field.Contains("faction", StringComparison.OrdinalIgnoreCase) ? "factions" : "items"));
        if (_currentSection.Kind == ReviewSectionKind.Output)
            return "Generated/user editable output path";

        return "Census/generated";
    }

    private string Provenance(string key)
    {
        return _spec is not null && _spec.Provenance.TryGetValue(key, out var source) ? source : "";
    }

    private void MarkProvenance(string key, string source)
    {
        if (_spec is not null && !string.IsNullOrWhiteSpace(key))
            _spec.Provenance[key] = source;
    }

    private string EditorKeyToProvenanceKey(string key)
    {
        if (_currentSection?.Kind == ReviewSectionKind.Step)
        {
            var step = GetStep(_currentSection);
            return key switch
            {
                "step.type" => $"step.{step.Number}.type",
                "step.description" => $"step.{step.Number}.description",
                "step.completed" => $"step.{step.Number}.completed",
                "step.quantityMin" => $"step.{step.Number}.quantityMin",
                "step.quantityMax" => $"step.{step.Number}.quantityMax",
                "step.percentage" => $"step.{step.Number}.percentage",
                "step.iconId" => $"step.{step.Number}.iconId",
                "step.iconName" => $"step.{step.Number}.iconName",
                "step.completionZone" => $"step.{step.Number}.completionZone",
                "step.searchText" => $"step.{step.Number}.searchText",
                var value when value.StartsWith("stepTarget.", StringComparison.OrdinalIgnoreCase) => $"step.{step.Number}.target",
                var value when value.StartsWith("location.", StringComparison.OrdinalIgnoreCase) => $"step.{step.Number}.location",
                var value when value.StartsWith("locationZone.", StringComparison.OrdinalIgnoreCase) => $"step.{step.Number}.location.zone",
                _ => key
            };
        }

        if (_currentSection?.Kind == ReviewSectionKind.Giver && key.StartsWith("giver.", StringComparison.OrdinalIgnoreCase))
            return "giver";
        if (_currentSection?.Kind == ReviewSectionKind.Quest && key.StartsWith("questId.", StringComparison.OrdinalIgnoreCase))
            return "questId";
        if (_currentSection?.Kind == ReviewSectionKind.Rewards)
            return key.StartsWith("rewards.", StringComparison.OrdinalIgnoreCase) ? key : "rewards";

        return key;
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        if (!string.IsNullOrWhiteSpace(status))
        {
            StatusText.Text = status;
            AppendLog(status);
        }

        RefreshActionStates();
    }

    private void SetStatus(string status)
    {
        StatusText.Text = status;
        AppendLog(status);
    }

    private void SetWorkflowEnabled(bool enabled)
    {
        SectionList.IsEnabled = enabled;
        RefreshActionStates();
    }

    private void RefreshActionStates()
    {
        var loaded = _spec is not null;
        FetchButton.IsEnabled = !_busy;
        NewTemplateButton.IsEnabled = !_busy;
        PreviewSpecButton.IsEnabled = !_busy;
        SettingsMenuItem.IsEnabled = !_busy;
        VisualEditorMenuItem.IsEnabled = !_busy;
        LayoutSettingsMenuItem.IsEnabled = !_busy;
        PreviousButton.IsEnabled = !_busy && loaded;
        NextButton.IsEnabled = !_busy && loaded;
        ResolveSectionButton.IsEnabled = !_busy && loaded;
        VerifyButton.IsEnabled = !_busy && loaded;
        GenerateButton.IsEnabled = !_busy && loaded;
        SectionList.IsEnabled = !_busy && loaded;
        UseCandidateButton.IsEnabled = !_busy && CurrentCandidate() is not null;
    }

    private void UpdateProgressText()
    {
        ProgressText.Text = _sections.Count == 0
            ? "Load or create a quest, then verify each section before generation."
            : $"{_verifiedSections.Count} of {_sections.Count} sections verified.";
    }

    private void AppendLog(string message)
    {
        var prefix = $"[{DateTime.Now:T}] ";
        LogBox.Text = string.IsNullOrEmpty(LogBox.Text)
            ? prefix + message
            : LogBox.Text + Environment.NewLine + prefix + message;
    }

    private string RequiredQuestName()
    {
        var value = (QuestNameBox.Text ?? "").Trim();
        if (value.Length == 0)
            throw new InvalidOperationException("Quest name is required.");
        return value;
    }

    private string RequiredSpecPath()
    {
        var value = (SpecPathBox.Text ?? "").Trim();
        if (value.Length == 0)
            throw new InvalidOperationException("Spec path is required.");
        return value;
    }

    private static string CleanPath(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string TrimForList(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "(no description)";
        text = text.ReplaceLineEndings(" ").Trim();
        return text.Length <= 64 ? text : text[..61] + "...";
    }

    private static string FormatSourceValue(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        text = text.ReplaceLineEndings(" ").Trim();
        return text.Length <= 44 ? text : text[..41] + "...";
    }

    private sealed record ReviewSection(
        string Key,
        string Label,
        ReviewSectionKind Kind,
        int StageIndex = -1,
        int StepIndex = -1);

    private sealed record SectionDisplay(bool Verified, string Label)
    {
        public override string ToString() => $"{(Verified ? "[x]" : "[ ]")} {Label}";
    }

    private sealed record ReferenceContext(string Label, int? OptionIndex, ResolvedReference Reference);

    private sealed record CandidateDisplay(string Context, int? OptionIndex, ResolveCandidate Candidate)
    {
        public override string ToString()
        {
            var context = string.IsNullOrWhiteSpace(Context) ? "" : Context + " | ";
            var metadata = Candidate.Metadata.Count == 0
                ? ""
                : " | " + string.Join("; ", Candidate.Metadata.Select(pair => pair.Key + "=" + pair.Value));
            return $"{context}{Candidate.Id} | {Candidate.Name} | {Candidate.Kind} | {Candidate.Zone} | {Candidate.Detail} | {Candidate.Source}{metadata}";
        }
    }

    private sealed record TemplateChoice(QuestTemplateKind Kind)
    {
        public override string ToString() => QuestTemplateFactory.DisplayName(Kind);
    }

    private enum ReviewSectionKind
    {
        Quest,
        Giver,
        Stage,
        Step,
        Rewards,
        Output
    }
}
