using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuestParser.Core;

namespace QuestParser.WinForms;

public sealed class MainForm : Form
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly QuestWorkflow _workflow = new();
    private readonly List<ReviewSection> _sections = [];
    private readonly HashSet<string> _verifiedSections = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Control> _editors = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirtyEditorKeys = new(StringComparer.OrdinalIgnoreCase);

    private QuestSpec? _spec;
    private ReviewSection? _currentSection;
    private bool _loadingSection;
    private bool _refreshingSectionList;

    private readonly TextBox _questNameBox = new();
    private readonly TextBox _authorBox = new();
    private readonly TextBox _contentRootBox = new();
    private readonly CheckBox _overwriteBox = new();
    private readonly Button _fetchButton = new();
    private readonly Button _browseContentRootButton = new();
    private readonly ComboBox _templateBox = new();
    private readonly Button _newTemplateButton = new();
    private readonly ListBox _sectionList = new();
    private readonly Label _sectionTitleLabel = new();
    private readonly Label _sectionHelpLabel = new();
    private readonly DataGridView _sourceGrid = new();
    private readonly Panel _editorHost = new();
    private readonly DataGridView _candidateGrid = new();
    private readonly Button _useCandidateButton = new();
    private readonly TextBox _missingSpawnBox = new();
    private readonly TextBox _sectionLuaBox = new();
    private readonly TextBox _luaPreviewBox = new();
    private readonly TextBox _sqlPreviewBox = new();
    private readonly TextBox _missingPreviewBox = new();
    private readonly TextBox _specPreviewBox = new();
    private readonly TextBox _censusQuestBox = new();
    private readonly TextBox _censusGiverBox = new();
    private readonly DataGridView _diagnosticsGrid = new();
    private readonly CheckBox _acknowledgeDiagnosticsBox = new();
    private readonly TextBox _logBox = new();
    private readonly Button _resolveSectionButton = new();
    private readonly Button _previousButton = new();
    private readonly Button _verifyButton = new();
    private readonly Button _nextButton = new();
    private readonly Button _generateButton = new();

    private DataGridView? _rewardItemsGrid;
    private DataGridView? _rewardFactionsGrid;

    public MainForm()
    {
        Text = "EQ2Emu QuestParser";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1520;
        Height = 960;
        MinimumSize = new Size(1180, 760);

        BuildUi();
        SetWorkflowEnabled(false);
        AppendLog("Ready. Enter a quest name, then fetch and resolve it before review.");
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        Controls.Add(root);

        root.Controls.Add(BuildTopPanel(), 0, 0);
        root.Controls.Add(BuildMainPanel(), 0, 1);
        root.Controls.Add(BuildButtonPanel(), 0, 2);
    }

    private Control BuildTopPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 9,
            RowCount = 2,
            Padding = new Padding(0, 0, 0, 8)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        _questNameBox.Dock = DockStyle.Fill;
        _questNameBox.PlaceholderText = "A Hunter's Tool";
        _questNameBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _fetchButton.PerformClick();
            }
        };

        _authorBox.Dock = DockStyle.Fill;
        _authorBox.PlaceholderText = "script author";

        _contentRootBox.Dock = DockStyle.Fill;
        _contentRootBox.Text = Defaults.ContentRoot;

        _overwriteBox.Text = "Overwrite files";
        _overwriteBox.AutoSize = true;
        _overwriteBox.Anchor = AnchorStyles.Left;

        _browseContentRootButton.Text = "Browse...";
        _browseContentRootButton.Dock = DockStyle.Fill;
        _browseContentRootButton.Click += (_, _) => BrowseContentRoot();

        _fetchButton.Text = "Fetch + Resolve";
        _fetchButton.Dock = DockStyle.Fill;
        _fetchButton.Click += FetchAndResolveAsync;

        _templateBox.Dock = DockStyle.Fill;
        _templateBox.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (var kind in Enum.GetValues<QuestTemplateKind>())
            _templateBox.Items.Add(new TemplateChoice(kind));
        _templateBox.SelectedIndex = 0;

        _newTemplateButton.Text = "New Template";
        _newTemplateButton.Dock = DockStyle.Fill;
        _newTemplateButton.Click += (_, _) => CreateTemplateQuest();

        panel.Controls.Add(CreateLabel("Quest"), 0, 0);
        panel.Controls.Add(_questNameBox, 1, 0);
        panel.Controls.Add(CreateLabel("Author"), 2, 0);
        panel.Controls.Add(_authorBox, 3, 0);
        panel.Controls.Add(_overwriteBox, 4, 0);
        panel.SetColumnSpan(_overwriteBox, 2);
        panel.Controls.Add(_fetchButton, 6, 0);

        panel.Controls.Add(CreateLabel("Content"), 0, 1);
        panel.Controls.Add(_contentRootBox, 1, 1);
        panel.SetColumnSpan(_contentRootBox, 4);
        panel.Controls.Add(_browseContentRootButton, 5, 1);
        panel.Controls.Add(_templateBox, 6, 1);
        panel.Controls.Add(_newTemplateButton, 7, 1);

        return panel;
    }

    private Control BuildMainPanel()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 310,
            FixedPanel = FixedPanel.Panel1
        };

        var leftPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2
        };
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        leftPanel.Controls.Add(new Label
        {
            Text = "Verification Steps",
            Dock = DockStyle.Fill,
            Font = new Font(Font, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        _sectionList.Dock = DockStyle.Fill;
        _sectionList.IntegralHeight = false;
        _sectionList.SelectedIndexChanged += (_, _) =>
        {
            if (_refreshingSectionList || _sectionList.SelectedIndex < 0)
                return;
            SelectSection(_sectionList.SelectedIndex);
        };
        leftPanel.Controls.Add(_sectionList, 0, 1);
        split.Panel1.Controls.Add(leftPanel);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildReviewPage());
        tabs.TabPages.Add(BuildGeneratedPage());
        tabs.TabPages.Add(BuildLogPage());
        split.Panel2.Controls.Add(tabs);

        return split;
    }

    private TabPage BuildReviewPage()
    {
        var page = new TabPage("Review");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 5,
            ColumnCount = 1,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 30));

        _sectionTitleLabel.Dock = DockStyle.Fill;
        _sectionTitleLabel.Font = new Font(Font.FontFamily, 13, FontStyle.Bold);
        _sectionTitleLabel.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(_sectionTitleLabel, 0, 0);

        _sectionHelpLabel.Dock = DockStyle.Fill;
        _sectionHelpLabel.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(_sectionHelpLabel, 0, 1);

        ConfigureReadOnlyGrid(_sourceGrid);
        layout.Controls.Add(WrapInGroup("Census and DB Data Used by This Section", _sourceGrid), 0, 2);

        _editorHost.Dock = DockStyle.Fill;
        _editorHost.AutoScroll = true;
        layout.Controls.Add(WrapInGroup("Editable Values", _editorHost), 0, 3);

        var lowerTabs = new TabControl { Dock = DockStyle.Fill };
        var candidatePage = new TabPage("DB Candidates");
        ConfigureReadOnlyGrid(_candidateGrid);
        _candidateGrid.SelectionChanged += (_, _) => _useCandidateButton.Enabled = CurrentCandidate() is not null;
        _useCandidateButton.Text = "Use Selected Candidate";
        _useCandidateButton.Dock = DockStyle.Bottom;
        _useCandidateButton.Height = 34;
        _useCandidateButton.Click += (_, _) => UseSelectedCandidate();
        candidatePage.Controls.Add(_candidateGrid);
        candidatePage.Controls.Add(_useCandidateButton);
        lowerTabs.TabPages.Add(candidatePage);

        var luaPage = new TabPage("Current Lua");
        ConfigurePreviewBox(_sectionLuaBox);
        luaPage.Controls.Add(_sectionLuaBox);
        lowerTabs.TabPages.Add(luaPage);

        var missingSpawnPage = new TabPage("Missing Spawn Wizard");
        ConfigurePreviewBox(_missingSpawnBox);
        _missingSpawnBox.Text = "Missing NPC guidance appears here when the current DB reference is a missing NPC.";
        missingSpawnPage.Controls.Add(_missingSpawnBox);
        lowerTabs.TabPages.Add(missingSpawnPage);

        layout.Controls.Add(lowerTabs, 0, 4);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildGeneratedPage()
    {
        var page = new TabPage("Generated / Raw Data");
        var tabs = new TabControl { Dock = DockStyle.Fill };

        ConfigurePreviewBox(_luaPreviewBox);
        ConfigurePreviewBox(_sqlPreviewBox);
        ConfigurePreviewBox(_missingPreviewBox);
        ConfigurePreviewBox(_specPreviewBox);
        ConfigurePreviewBox(_censusQuestBox);
        ConfigurePreviewBox(_censusGiverBox);
        ConfigureReadOnlyGrid(_diagnosticsGrid);

        tabs.TabPages.Add(CreateDiagnosticsPage());
        tabs.TabPages.Add(CreateTextPage("Lua", _luaPreviewBox));
        tabs.TabPages.Add(CreateTextPage("SQL", _sqlPreviewBox));
        tabs.TabPages.Add(CreateTextPage("Missing", _missingPreviewBox));
        tabs.TabPages.Add(CreateTextPage("Spec JSON", _specPreviewBox));
        tabs.TabPages.Add(CreateTextPage("Census Quest JSON", _censusQuestBox));
        tabs.TabPages.Add(CreateTextPage("Census Questgiver JSON", _censusGiverBox));

        page.Controls.Add(tabs);
        return page;
    }

    private TabPage CreateDiagnosticsPage()
    {
        var page = new TabPage("Diagnostics");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.Controls.Add(_diagnosticsGrid, 0, 0);

        _acknowledgeDiagnosticsBox.Text = "I have reviewed the remaining blockers and want to allow generation";
        _acknowledgeDiagnosticsBox.Dock = DockStyle.Fill;
        _acknowledgeDiagnosticsBox.CheckedChanged += (_, _) => RefreshDiagnostics();
        layout.Controls.Add(_acknowledgeDiagnosticsBox, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildLogPage()
    {
        var page = new TabPage("Log");
        ConfigurePreviewBox(_logBox);
        page.Controls.Add(_logBox);
        return page;
    }

    private Control BuildButtonPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0)
        };

        _generateButton.Text = "Generate Files";
        _generateButton.Width = 132;
        _generateButton.Click += GenerateFilesAsync;

        _resolveSectionButton.Text = "Resolve Section";
        _resolveSectionButton.Width = 128;
        _resolveSectionButton.Click += ResolveCurrentSectionAsync;

        _nextButton.Text = "Next";
        _nextButton.Width = 96;
        _nextButton.Click += (_, _) => MoveSection(1);

        _verifyButton.Text = "Verify Section";
        _verifyButton.Width = 124;
        _verifyButton.Click += (_, _) => VerifyCurrentSection();

        _previousButton.Text = "Previous";
        _previousButton.Width = 96;
        _previousButton.Click += (_, _) => MoveSection(-1);

        panel.Controls.Add(_generateButton);
        panel.Controls.Add(_nextButton);
        panel.Controls.Add(_verifyButton);
        panel.Controls.Add(_resolveSectionButton);
        panel.Controls.Add(_previousButton);
        return panel;
    }

    private async void FetchAndResolveAsync(object? sender, EventArgs e)
    {
        var questName = _questNameBox.Text.Trim();
        if (questName.Length == 0)
        {
            MessageBox.Show(this, "Enter a quest name first.", "QuestParser", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            SetBusy(true);
            ClearLoadedQuestState();
            AppendLog($"Fetching quest source data for '{questName}'.");

            var imported = await _workflow.ImportAsync(
                questName,
                CleanPath(_contentRootBox.Text, Defaults.ContentRoot),
                _authorBox.Text.Trim());

            AppendLog($"Quest source import created spec: {imported.Spec.Output.SpecPath}");
            AppendLog("Resolving DB references from MariaDB.");
            var resolved = await _workflow.ResolveAsync(imported.Spec.Output.SpecPath);

            _spec = resolved.Spec;
            await LoadRawCensusTabsAsync(questName);
            RebuildSections();
            SetWorkflowEnabled(true);
            SelectSection(0);
            RefreshPreview();
            AppendLog("Review is ready. Verify each section before generating files.");
        }
        catch (Exception ex)
        {
            AppendLog(ex.ToString());
            MessageBox.Show(this, ex.Message, "Fetch failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void CreateTemplateQuest()
    {
        var questName = _questNameBox.Text.Trim();
        if (questName.Length == 0)
        {
            MessageBox.Show(this, "Enter a quest name before creating a manual template.", "QuestParser", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var choice = _templateBox.SelectedItem as TemplateChoice ?? new TemplateChoice(QuestTemplateKind.Blank);
            ClearLoadedQuestState();
            var result = _workflow.CreateTemplate(
                choice.Kind,
                questName,
                "Uncategorized",
                CleanPath(_contentRootBox.Text, Defaults.ContentRoot),
                _authorBox.Text.Trim());

            _spec = result.Spec;
            _censusQuestBox.Text = "Manual template. No quest source payload was fetched.";
            _censusGiverBox.Text = "Manual template. No questgiver source payload was fetched.";
            RebuildSections();
            SetWorkflowEnabled(true);
            SelectSection(0);
            RefreshPreview();
            AppendLog($"Created manual template '{QuestTemplateFactory.DisplayName(choice.Kind)}' for '{questName}'. Edit fields, then use Resolve Section where needed.");
        }
        catch (Exception ex)
        {
            AppendLog(ex.ToString());
            MessageBox.Show(this, ex.Message, "Template failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void GenerateFilesAsync(object? sender, EventArgs e)
    {
        if (_spec is null)
            return;

        try
        {
            SaveCurrentSection();
            var diagnostics = QuestSpecValidator.Validate(_spec, _overwriteBox.Checked);
            RefreshDiagnostics(diagnostics);
            var blockers = diagnostics.Where(diagnostic => diagnostic.Severity == QuestDiagnosticSeverity.Blocker).ToArray();
            if (blockers.Length > 0 && !_acknowledgeDiagnosticsBox.Checked)
            {
                MessageBox.Show(
                    this,
                    "Diagnostics found blockers. Fix them or acknowledge them on the Diagnostics tab before generation.\r\n\r\nFirst blocker: " + blockers[0].Message,
                    "Diagnostics review required",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                SelectSectionByKey(blockers[0].SectionKey);
                return;
            }

            var unverified = _sections.Where(section => !_verifiedSections.Contains(section.Key)).Select(section => section.Label).ToArray();
            if (unverified.Length > 0)
            {
                MessageBox.Show(
                    this,
                    "Every section needs manual verification before file generation.\r\n\r\nFirst unverified section: " + unverified[0],
                    "Verification required",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                SelectSection(_sections.FindIndex(section => string.Equals(section.Label, unverified[0], StringComparison.Ordinal)));
                return;
            }

            SetBusy(true);
            AppendLog("Generating Lua, SQL, spec, and missing-data report from verified UI values.");
            var result = await _workflow.GenerateFromSpecAsync(_spec, _overwriteBox.Checked);
            _spec = result.Spec;
            RefreshPreview();
            AppendLog("Generated files:");
            foreach (var file in result.WrittenFiles)
                AppendLog("  " + file);

            MessageBox.Show(this, "Quest files generated successfully.", "QuestParser", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendLog(ex.ToString());
            MessageBox.Show(this, ex.Message, "Generate failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ResolveCurrentSectionAsync(object? sender, EventArgs e)
    {
        if (_spec is null || _currentSection is null)
            return;

        try
        {
            SaveCurrentSection();
            SetBusy(true);
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
                case ReviewSectionKind.Output:
                case ReviewSectionKind.Stage:
                    AppendLog("This section has no DB reference to resolve.");
                    return;
            }

            _verifiedSections.Remove(_currentSection.Key);
            var index = _sectionList.SelectedIndex;
            LoadSection(_currentSection);
            RefreshSectionList(index);
            RefreshPreview();
            AppendLog("Section resolution complete. Review candidates or resolved values before verifying.");
        }
        catch (Exception ex)
        {
            AppendLog(ex.ToString());
            MessageBox.Show(this, ex.Message, "Resolve failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadRawCensusTabsAsync(string questName)
    {
        _censusQuestBox.Text = await ReadIfExistsAsync(Path.Combine(Defaults.CensusCacheDirectory, CensusClient.QuestJsonFileName(questName)));
        _censusGiverBox.Text = await ReadIfExistsAsync(Path.Combine(Defaults.CensusCacheDirectory, CensusClient.QuestGiverJsonFileName(questName)));
    }

    private static async Task<string> ReadIfExistsAsync(string path)
    {
        return File.Exists(path)
            ? await File.ReadAllTextAsync(path)
            : $"No cached file found at {path}";
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

    private void ClearLoadedQuestState()
    {
        var wasLoadingSection = _loadingSection;
        var wasRefreshingSectionList = _refreshingSectionList;
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
            _rewardItemsGrid = null;
            _rewardFactionsGrid = null;

            _sectionList.Items.Clear();
            _sectionTitleLabel.Text = "";
            _sectionHelpLabel.Text = "";
            _editorHost.Controls.Clear();
            _sourceGrid.Rows.Clear();
            _candidateGrid.Rows.Clear();
            _useCandidateButton.Enabled = false;
            _missingSpawnBox.Text = "Missing NPC guidance appears here when the current DB reference is a missing NPC.";
            _sectionLuaBox.Clear();
            _luaPreviewBox.Clear();
            _sqlPreviewBox.Clear();
            _missingPreviewBox.Clear();
            _specPreviewBox.Clear();
            _censusQuestBox.Clear();
            _censusGiverBox.Clear();
            _diagnosticsGrid.Rows.Clear();
            _acknowledgeDiagnosticsBox.Checked = false;

            SetWorkflowEnabled(false);
        }
        finally
        {
            _refreshingSectionList = wasRefreshingSectionList;
            _loadingSection = wasLoadingSection;
        }
    }

    private void SelectSection(int index)
    {
        if (_spec is null || index < 0 || index >= _sections.Count)
            return;

        if (!_loadingSection)
            SaveCurrentSection();

        RefreshSectionList(index);
        LoadSection(_sections[index]);
    }

    private void SelectSectionByKey(string sectionKey)
    {
        var index = _sections.FindIndex(section => string.Equals(section.Key, sectionKey, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            SelectSection(index);
    }

    private void RefreshSectionList(int selectedIndex)
    {
        _refreshingSectionList = true;
        _sectionList.Items.Clear();
        for (var i = 0; i < _sections.Count; i++)
        {
            var section = _sections[i];
            var marker = _verifiedSections.Contains(section.Key) ? "[x]" : "[ ]";
            _sectionList.Items.Add($"{marker} {section.Label}");
        }

        if (selectedIndex >= 0 && selectedIndex < _sectionList.Items.Count)
            _sectionList.SelectedIndex = selectedIndex;
        _refreshingSectionList = false;
    }

    private void LoadSection(ReviewSection section)
    {
        if (_spec is null)
            return;

        _loadingSection = true;
        _currentSection = section;
        _editors.Clear();
        _rewardItemsGrid = null;
        _rewardFactionsGrid = null;
        _editorHost.Controls.Clear();
        _sourceGrid.Rows.Clear();
        _candidateGrid.Rows.Clear();
        _useCandidateButton.Enabled = false;
        _missingSpawnBox.Text = "Missing NPC guidance appears here when the current DB reference is a missing NPC.";

        _sectionTitleLabel.Text = section.Label;
        _sectionHelpLabel.Text = "Review the Census source values, DB resolution, and generated Lua. Edit anything that is wrong, then press Verify Section.";

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
        _loadingSection = false;
        RefreshPreview();
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
            ("DB quest id", spec.QuestId.Id?.ToString() ?? ""));

        var table = CreateEditorTable();
        AddHeader(table, "Quest metadata");
        AddTextEditor(table, "quest.name", "Quest name", spec.Quest.Name);
        AddTextEditor(table, "quest.zone", "Zone/category", spec.Quest.Zone);
        AddNumericEditor(table, "quest.level", "Level", spec.Quest.Level, 0, 255);
        AddNumericEditor(table, "quest.tier", "Tier", spec.Quest.Tier, 0, 255);
        AddTextEditor(table, "quest.author", "Author", spec.Quest.Author);
        AddBoolEditor(table, "quest.repeatable", "Repeatable", spec.Quest.Repeatable);
        AddBoolEditor(table, "quest.shareable", "Shareable", spec.Quest.Shareable);
        AddBoolEditor(table, "quest.completeShareable", "Complete shareable", spec.Quest.CompleteShareable);
        AddBoolEditor(table, "quest.tradeskill", "Tradeskill", spec.Quest.IsTradeskill);
        AddBoolEditor(table, "quest.scales", "Scales with level", spec.Quest.ScalesWithLevel);
        AddTextEditor(table, "quest.starter", "Starter text", spec.Quest.StarterText, multiline: true);
        AddTextEditor(table, "quest.completion", "Completion text", spec.Quest.CompletionText, multiline: true);
        AddHeader(table, "Database quest ID");
        AddReferenceEditor(table, "questId", spec.QuestId);
        _editorHost.Controls.Add(table);
    }

    private void LoadGiverSection()
    {
        var spec = RequireSpec();
        SetSourceRows(
            ("Census questgivers", string.Join(", ", spec.QuestGivers)),
            ("Selected query", spec.Giver.Query),
            ("DB status", spec.Giver.Status),
            ("DB id", spec.Giver.Id?.ToString() ?? ""),
            ("DB name", spec.Giver.Name),
            ("Candidates", spec.Giver.Candidates.Count));

        var table = CreateEditorTable();
        AddHeader(table, "Quest giver");
        AddTextEditor(table, "giver.list", "Census questgivers", string.Join(", ", spec.QuestGivers), multiline: true);
        AddReferenceEditor(table, "giver", spec.Giver);
        _editorHost.Controls.Add(table);
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

        var table = CreateEditorTable();
        AddHeader(table, $"Stage {stage.Number}");
        AddTextEditor(table, "stage.description", "Task group text", stage.Description, multiline: true);
        AddTextEditor(table, "stage.completed", "Completed group text", stage.CompletedDescription, multiline: true);
        AddBoolEditor(table, "stage.parallel", "Parallel stage", stage.IsParallel);
        _editorHost.Controls.Add(table);
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
            ("Target status", step.Target.Status),
            ("Target id", step.Target.Id?.ToString() ?? ""),
            ("Target name", step.Target.Name),
            ("Candidates", step.Target.Candidates.Count));

        var table = CreateEditorTable();
        AddHeader(table, $"Step {step.Number}");
        AddComboEditor(table, "step.type", "Lua step function", step.Type);
        AddTextEditor(table, "step.description", "Description", step.Description, multiline: true);
        AddTextEditor(table, "step.completed", "Completed text", step.CompletedDescription, multiline: true);
        AddNumericEditor(table, "step.quantityMin", "Quantity min", step.QuantityMin, 0, 100000);
        AddNumericEditor(table, "step.quantityMax", "Quantity max", step.QuantityMax, 0, 100000);
        AddNumericEditor(table, "step.percentage", "Percentage", Convert.ToDecimal(step.Percentage), 0, 100, 2);
        AddNumericEditor(table, "step.iconId", "Icon id", step.IconId, 0, 1000000);
        AddTextEditor(table, "step.iconName", "Icon name", step.IconName);
        AddTextEditor(table, "step.completionZone", "Completion zone", step.CompletionZone);
        AddTextEditor(table, "step.searchText", "DB search text", step.SearchText);
        AddHeader(table, "Step target DB reference");
        AddReferenceEditor(table, "stepTarget", step.Target);

        if (step.Type is StepType.Location or StepType.ZoneLocation)
        {
            step.Location ??= new LocationTarget();
            AddHeader(table, "Location");
            AddNumericEditor(table, "location.x", "X", Convert.ToDecimal(step.Location.X), -100000, 100000, 3);
            AddNumericEditor(table, "location.y", "Y", Convert.ToDecimal(step.Location.Y), -100000, 100000, 3);
            AddNumericEditor(table, "location.z", "Z", Convert.ToDecimal(step.Location.Z), -100000, 100000, 3);
            AddNumericEditor(table, "location.radius", "Radius", Convert.ToDecimal(step.Location.Radius), 0, 100000, 3);
            AddReferenceEditor(table, "locationZone", step.Location.Zone);
        }

        _editorHost.Controls.Add(table);
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

        var table = CreateEditorTable();
        AddHeader(table, "Coin and experience");
        AddNumericEditor(table, "rewards.coinMin", "Coin min", spec.Rewards.CoinMin, 0, int.MaxValue);
        AddNumericEditor(table, "rewards.coinMax", "Coin max", spec.Rewards.CoinMax, 0, int.MaxValue);
        AddNumericEditor(table, "rewards.xp", "Experience", Convert.ToDecimal(spec.Rewards.Experience), 0, int.MaxValue, 2);

        AddHeader(table, "Reward items");
        _rewardItemsGrid = CreateEditableGrid();
        _rewardItemsGrid.Columns.Add("quantity", "Quantity");
        _rewardItemsGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "selectable", HeaderText = "Selectable" });
        _rewardItemsGrid.Columns.Add("status", "Status");
        _rewardItemsGrid.Columns.Add("id", "DB/Census Item ID");
        _rewardItemsGrid.Columns.Add("name", "Name");
        _rewardItemsGrid.Columns.Add("query", "Query");
        _rewardItemsGrid.Columns.Add("kind", "Kind");
        foreach (var item in spec.Rewards.Items)
        {
            _rewardItemsGrid.Rows.Add(
                item.Quantity,
                item.IsSelectable,
                item.Item.Status.ToString(),
                item.Item.Id?.ToString() ?? "",
                item.Item.Name,
                item.Item.Query,
                string.IsNullOrWhiteSpace(item.Item.Kind) ? "item" : item.Item.Kind);
        }
        AddGrid(table, _rewardItemsGrid, 150);

        AddHeader(table, "Reward factions");
        _rewardFactionsGrid = CreateEditableGrid();
        _rewardFactionsGrid.Columns.Add("amount", "Amount");
        _rewardFactionsGrid.Columns.Add("status", "Status");
        _rewardFactionsGrid.Columns.Add("id", "Faction ID");
        _rewardFactionsGrid.Columns.Add("name", "Name");
        _rewardFactionsGrid.Columns.Add("query", "Query");
        _rewardFactionsGrid.Columns.Add("kind", "Kind");
        foreach (var faction in spec.Rewards.Factions)
        {
            _rewardFactionsGrid.Rows.Add(
                faction.Amount,
                faction.Faction.Status.ToString(),
                faction.Faction.Id?.ToString() ?? "",
                faction.Faction.Name,
                faction.Faction.Query,
                string.IsNullOrWhiteSpace(faction.Faction.Kind) ? "faction" : faction.Faction.Kind);
        }
        AddGrid(table, _rewardFactionsGrid, 150);

        _editorHost.Controls.Add(table);
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

        var table = CreateEditorTable();
        AddHeader(table, "Output paths");
        AddTextEditor(table, "output.contentRoot", "Content root", spec.Output.ContentRoot);
        AddTextEditor(table, "output.questDirectory", "Quest directory", spec.Output.QuestDirectory);
        AddTextEditor(table, "output.lua", "Lua path", spec.Output.LuaPath);
        AddTextEditor(table, "output.spawnScript", "Spawn script example path", spec.Output.SpawnScriptPath);
        AddTextEditor(table, "output.spec", "Spec JSON path", spec.Output.SpecPath);
        AddTextEditor(table, "output.sql", "SQL path", spec.Output.SqlPath);
        AddTextEditor(table, "output.missing", "Missing report path", spec.Output.MissingReportPath);
        AddTextEditor(table, "output.preview", "Runtime preview path", spec.Output.PreviewPath);
        _editorHost.Controls.Add(table);
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
        step.Percentage = Convert.ToSingle(ReadDecimal("step.percentage"));
        step.IconId = ReadInt("step.iconId");
        step.IconName = ReadText("step.iconName");
        step.CompletionZone = ReadText("step.completionZone");
        step.SearchText = ReadText("step.searchText");
        SaveReference("stepTarget", step.Target);

        if (_editors.ContainsKey("location.x"))
        {
            step.Location ??= new LocationTarget();
            step.Location.X = Convert.ToSingle(ReadDecimal("location.x"));
            step.Location.Y = Convert.ToSingle(ReadDecimal("location.y"));
            step.Location.Z = Convert.ToSingle(ReadDecimal("location.z"));
            step.Location.Radius = Convert.ToSingle(ReadDecimal("location.radius"));
            SaveReference("locationZone", step.Location.Zone);
        }
    }

    private void SaveRewardsSection()
    {
        var spec = RequireSpec();
        spec.Rewards.CoinMin = ReadInt("rewards.coinMin");
        spec.Rewards.CoinMax = ReadInt("rewards.coinMax");
        spec.Rewards.Experience = Convert.ToDouble(ReadDecimal("rewards.xp"));

        if (_rewardItemsGrid is not null)
        {
            _rewardItemsGrid.EndEdit();
            spec.Rewards.Items = _rewardItemsGrid.Rows
                .Cast<DataGridViewRow>()
                .Where(row => !row.IsNewRow)
                .Select(ReadRewardItem)
                .Where(item => item is not null)
                .Cast<RewardItemSpec>()
                .ToList();
        }

        if (_rewardFactionsGrid is not null)
        {
            _rewardFactionsGrid.EndEdit();
            spec.Rewards.Factions = _rewardFactionsGrid.Rows
                .Cast<DataGridViewRow>()
                .Where(row => !row.IsNewRow)
                .Select(ReadRewardFaction)
                .Where(faction => faction is not null)
                .Cast<RewardFactionSpec>()
                .ToList();
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
    }

    private RewardItemSpec? ReadRewardItem(DataGridViewRow row)
    {
        var quantity = ReadCellInt(row, "quantity");
        var id = ReadCellNullableInt(row, "id");
        var name = ReadCellString(row, "name");
        var query = ReadCellString(row, "query");
        if (quantity == 0 && id is null && name.Length == 0 && query.Length == 0)
            return null;

        return new RewardItemSpec
        {
            Quantity = quantity <= 0 ? 1 : quantity,
            IsSelectable = ReadCellBool(row, "selectable"),
            Item = BuildReference(
                ReadCellString(row, "kind", "item"),
                query,
                ReadCellStatus(row, "status"),
                id,
                name)
        };
    }

    private RewardFactionSpec? ReadRewardFaction(DataGridViewRow row)
    {
        var amount = ReadCellInt(row, "amount");
        var id = ReadCellNullableInt(row, "id");
        var name = ReadCellString(row, "name");
        var query = ReadCellString(row, "query");
        if (amount == 0 && id is null && name.Length == 0 && query.Length == 0)
            return null;

        return new RewardFactionSpec
        {
            Amount = amount,
            Faction = BuildReference(
                ReadCellString(row, "kind", "faction"),
                query,
                ReadCellStatus(row, "status"),
                id,
                name)
        };
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
        if (_sectionList.SelectedIndex < 0)
            return;
        SelectSection(Math.Clamp(_sectionList.SelectedIndex + delta, 0, _sections.Count - 1));
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
        _acknowledgeDiagnosticsBox.Checked = false;
        var index = _sections.FindIndex(section => section.Key == _currentSection.Key);
        RefreshSectionList(index);
        RefreshPreview();
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

    private void RefreshPreview()
    {
        if (_spec is null)
        {
            _luaPreviewBox.Clear();
            _sqlPreviewBox.Clear();
            _missingPreviewBox.Clear();
            _specPreviewBox.Clear();
            _sectionLuaBox.Clear();
            return;
        }

        try
        {
            var preview = _workflow.Preview(_spec);
            _luaPreviewBox.Text = preview.Lua;
            _sqlPreviewBox.Text = preview.Sql;
            _missingPreviewBox.Text = preview.MissingReport;
            _specPreviewBox.Text = JsonSerializer.Serialize(_spec, JsonOptions);
            _sectionLuaBox.Text = BuildSectionLuaPreview(preview.Lua);
            RefreshDiagnostics();
        }
        catch (Exception ex)
        {
            _sectionLuaBox.Text = ex.Message;
        }
    }

    private void RefreshDiagnostics(List<QuestDiagnostic>? diagnostics = null)
    {
        _diagnosticsGrid.Rows.Clear();
        EnsureDiagnosticColumns();
        if (_spec is null)
            return;

        diagnostics ??= QuestSpecValidator.Validate(_spec, _overwriteBox.Checked);
        foreach (var diagnostic in diagnostics)
            _diagnosticsGrid.Rows.Add(diagnostic.Severity, diagnostic.SectionKey, diagnostic.Code, diagnostic.Message);
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

        if (_currentSection.Kind == ReviewSectionKind.Quest || _currentSection.Kind == ReviewSectionKind.Giver)
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
        _candidateGrid.Rows.Clear();
        var reference = CurrentReference();
        if (reference is null)
            return;

        EnsureCandidateColumns();
        foreach (var candidate in reference.Candidates)
        {
            var rowIndex = _candidateGrid.Rows.Add(candidate.Id, candidate.Name, candidate.Kind, candidate.Zone, candidate.Detail, candidate.Source, FormatMetadata(candidate.Metadata));
            _candidateGrid.Rows[rowIndex].Tag = candidate;
        }
        RefreshMissingSpawnWizard(reference);
    }

    private void RefreshMissingSpawnWizard(ResolvedReference reference)
    {
        if (_spec is null)
            return;

        if (!string.Equals(reference.Kind, "npc", StringComparison.OrdinalIgnoreCase) || reference.Status != ResolveStatus.Missing)
        {
            _missingSpawnBox.Text = "Missing NPC guidance appears here when the current DB reference is a missing NPC.";
            return;
        }

        var context = _currentSection?.Label ?? "Current section";
        var template = MissingSpawnTemplateBuilder.Build(_spec, reference, context);
        _missingSpawnBox.Text = MissingSpawnTemplateBuilder.Format(template);
    }

    private void UseSelectedCandidate()
    {
        var reference = CurrentReference();
        var candidate = CurrentCandidate();
        if (reference is null || candidate is null)
            return;

        reference.Status = ResolveStatus.Resolved;
        reference.Id = candidate.Id;
        reference.Ids = [candidate.Id];
        reference.Name = candidate.Name;
        reference.Kind = string.IsNullOrWhiteSpace(candidate.Kind) ? reference.Kind : candidate.Kind;
        reference.Source = "User selected DB candidate";
        reference.Metadata = new Dictionary<string, string>(candidate.Metadata, StringComparer.OrdinalIgnoreCase);
        reference.Candidates.Clear();

        if (_currentSection?.Kind == ReviewSectionKind.Step)
        {
            var step = GetStep(_currentSection);
            step.Target = reference;
            MarkProvenance($"step.{step.Number}.target", "User selected DB candidate");
        }
        else if (_currentSection?.Kind == ReviewSectionKind.Quest)
        {
            MarkProvenance("questId", "User selected DB candidate");
        }
        else if (_currentSection?.Kind == ReviewSectionKind.Giver)
        {
            MarkProvenance("giver", "User selected DB candidate");
        }

        if (_currentSection is not null)
            _verifiedSections.Remove(_currentSection.Key);

        var index = _sectionList.SelectedIndex;
        LoadSection(_currentSection!);
        RefreshSectionList(index);
        AppendLog($"Selected candidate {candidate.Id}: {candidate.Name}");
    }

    private ResolveCandidate? CurrentCandidate()
    {
        return _candidateGrid.SelectedRows.Count == 0
            ? null
            : _candidateGrid.SelectedRows[0].Tag as ResolveCandidate;
    }

    private ResolvedReference? CurrentReference()
    {
        if (_spec is null || _currentSection is null)
            return null;

        return _currentSection.Kind switch
        {
            ReviewSectionKind.Quest => _spec.QuestId,
            ReviewSectionKind.Giver => _spec.Giver,
            ReviewSectionKind.Step => GetStep(_currentSection).Target,
            _ => null
        };
    }

    private void SaveReference(string prefix, ResolvedReference reference)
    {
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

    private static ResolvedReference BuildReference(string kind, string query, ResolveStatus status, int? id, string name)
    {
        return new ResolvedReference
        {
            Kind = string.IsNullOrWhiteSpace(kind) ? "unknown" : kind,
            Query = query,
            Status = status,
            Id = id,
            Name = name,
            Source = "User override",
            Ids = id.HasValue ? [id.Value] : []
        };
    }

    private void AddReferenceEditor(TableLayoutPanel table, string prefix, ResolvedReference reference)
    {
        AddTextEditor(table, prefix + ".kind", "Kind", reference.Kind);
        AddTextEditor(table, prefix + ".query", "Query/search text", reference.Query);
        AddComboEditor(table, prefix + ".status", "Status", reference.Status);
        AddTextEditor(table, prefix + ".id", "ID", reference.Id?.ToString() ?? "");
        AddTextEditor(table, prefix + ".name", "Resolved name", reference.Name);
        AddTextEditor(table, prefix + ".source", "Source", reference.Source);
    }

    private TableLayoutPanel CreateEditorTable()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(4)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return table;
    }

    private void AddHeader(TableLayoutPanel table, string text)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Padding = new Padding(0, 10, 0, 4)
        };
        table.Controls.Add(label, 0, row);
        table.SetColumnSpan(label, 2);
    }

    private void AddTextEditor(TableLayoutPanel table, string key, string labelText, string value, bool multiline = false)
    {
        var box = new TextBox
        {
            Text = value,
            Dock = DockStyle.Fill,
            Multiline = multiline,
            ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
            Height = multiline ? 78 : 26
        };
        box.TextChanged += (_, _) => EditorChanged(key);
        AddEditorControl(table, key, labelText, box, multiline ? 86 : 30);
    }

    private void AddBoolEditor(TableLayoutPanel table, string key, string labelText, bool value)
    {
        var check = new CheckBox
        {
            Checked = value,
            Dock = DockStyle.Left,
            AutoSize = true
        };
        check.CheckedChanged += (_, _) => EditorChanged(key);
        AddEditorControl(table, key, labelText, check, 30);
    }

    private void AddComboEditor<T>(TableLayoutPanel table, string key, string labelText, T value) where T : struct, Enum
    {
        var combo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        combo.Items.AddRange(Enum.GetNames(typeof(T)));
        combo.SelectedItem = value.ToString();
        combo.SelectedIndexChanged += (_, _) => EditorChanged(key);
        AddEditorControl(table, key, labelText, combo, 30);
    }

    private void AddNumericEditor(TableLayoutPanel table, string key, string labelText, decimal value, decimal minimum, decimal maximum, int decimalPlaces = 0)
    {
        var number = new NumericUpDown
        {
            Minimum = minimum,
            Maximum = maximum,
            DecimalPlaces = decimalPlaces,
            Increment = decimalPlaces > 0 ? 0.1m : 1m,
            Value = Math.Clamp(value, minimum, maximum),
            Dock = DockStyle.Left,
            Width = 160
        };
        number.ValueChanged += (_, _) => EditorChanged(key);
        AddEditorControl(table, key, labelText, number, 30);
    }

    private void AddGrid(TableLayoutPanel table, DataGridView grid, int height)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        table.Controls.Add(grid, 0, row);
        table.SetColumnSpan(grid, 2);
    }

    private void AddEditorControl(TableLayoutPanel table, string key, string labelText, Control control, int height)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        table.Controls.Add(new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, row);
        table.Controls.Add(control, 1, row);
        _editors[key] = control;
    }

    private DataGridView CreateEditableGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            EditMode = DataGridViewEditMode.EditOnEnter
        };
        grid.CellValueChanged += (_, _) => EditorChanged();
        grid.UserDeletedRow += (_, _) => EditorChanged();
        grid.RowsAdded += (_, _) =>
        {
            if (!_loadingSection)
                EditorChanged();
        };
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty)
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        return grid;
    }

    private string ReadText(string key)
    {
        return _editors.TryGetValue(key, out var control)
            ? control switch
            {
                TextBox box => box.Text.Trim(),
                ComboBox combo => Convert.ToString(combo.SelectedItem) ?? "",
                _ => control.Text.Trim()
            }
            : "";
    }

    private int ReadInt(string key)
    {
        if (!_editors.TryGetValue(key, out var control))
            return 0;
        if (control is NumericUpDown number)
            return Convert.ToInt32(number.Value);
        return int.TryParse(ReadText(key), out var value) ? value : 0;
    }

    private int? ReadNullableInt(string key)
    {
        var text = ReadText(key);
        return int.TryParse(text, out var value) ? value : null;
    }

    private decimal ReadDecimal(string key)
    {
        if (!_editors.TryGetValue(key, out var control))
            return 0;
        if (control is NumericUpDown number)
            return number.Value;
        return decimal.TryParse(ReadText(key), out var value) ? value : 0;
    }

    private bool ReadBool(string key)
    {
        return _editors.TryGetValue(key, out var control) && control is CheckBox check && check.Checked;
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
        _sourceGrid.Rows.Clear();
        EnsureSourceColumns();
        foreach (var (field, value) in rows)
            _sourceGrid.Rows.Add(field, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "", SourceForField(field));
    }

    private void EnsureSourceColumns()
    {
        if (_sourceGrid.Columns.Count > 0)
            return;
        _sourceGrid.Columns.Add("field", "Field");
        _sourceGrid.Columns.Add("value", "Value");
        _sourceGrid.Columns.Add("source", "Source");
    }

    private string SourceForField(string field)
    {
        if (_spec is null || _currentSection is null)
            return "";

        if (field.StartsWith("Census", StringComparison.OrdinalIgnoreCase))
            return "Census";
        if (field.StartsWith("DB", StringComparison.OrdinalIgnoreCase) || field.Contains("Candidates", StringComparison.OrdinalIgnoreCase))
            return CurrentReference()?.Source ?? "DB resolver";

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
            return CurrentReference()?.Source ?? Provenance("giver.query");
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

    private void EnsureCandidateColumns()
    {
        if (_candidateGrid.Columns.Count > 0)
            return;
        _candidateGrid.Columns.Add("id", "ID");
        _candidateGrid.Columns.Add("name", "Name");
        _candidateGrid.Columns.Add("kind", "Kind");
        _candidateGrid.Columns.Add("zone", "Zone");
        _candidateGrid.Columns.Add("detail", "Detail");
        _candidateGrid.Columns.Add("source", "Source");
        _candidateGrid.Columns.Add("metadata", "Metadata");
    }

    private void EnsureDiagnosticColumns()
    {
        if (_diagnosticsGrid.Columns.Count > 0)
            return;
        _diagnosticsGrid.Columns.Add("severity", "Severity");
        _diagnosticsGrid.Columns.Add("section", "Section");
        _diagnosticsGrid.Columns.Add("code", "Code");
        _diagnosticsGrid.Columns.Add("message", "Message");
    }

    private static string ReadCellString(DataGridViewRow row, string columnName, string fallback = "")
    {
        var value = row.Cells[columnName].Value;
        var text = Convert.ToString(value)?.Trim();
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }

    private static int ReadCellInt(DataGridViewRow row, string columnName)
    {
        return int.TryParse(ReadCellString(row, columnName), out var value) ? value : 0;
    }

    private static int? ReadCellNullableInt(DataGridViewRow row, string columnName)
    {
        return int.TryParse(ReadCellString(row, columnName), out var value) ? value : null;
    }

    private static bool ReadCellBool(DataGridViewRow row, string columnName)
    {
        var value = row.Cells[columnName].Value;
        return value is bool boolean
            ? boolean
            : bool.TryParse(Convert.ToString(value), out var parsed) && parsed;
    }

    private static ResolveStatus ReadCellStatus(DataGridViewRow row, string columnName)
    {
        return Enum.TryParse<ResolveStatus>(ReadCellString(row, columnName), out var status)
            ? status
            : ResolveStatus.Missing;
    }

    private static void ConfigureReadOnlyGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.ReadOnly = true;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
    }

    private static void ConfigurePreviewBox(TextBox box)
    {
        box.Dock = DockStyle.Fill;
        box.Multiline = true;
        box.ScrollBars = ScrollBars.Both;
        box.WordWrap = false;
        box.Font = new Font("Consolas", 9);
    }

    private static TabPage CreateTextPage(string title, Control control)
    {
        var page = new TabPage(title);
        page.Controls.Add(control);
        return page;
    }

    private static Control WrapInGroup(string title, Control content)
    {
        var group = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        group.Controls.Add(content);
        return group;
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private static Label CreateSmallPathLabel()
    {
        return new Label
        {
            Text = "runtime data stays beside the UI exe",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.GrayText
        };
    }

    private void BrowseContentRoot()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose EQ2Emu content root",
            InitialDirectory = Directory.Exists(_contentRootBox.Text) ? _contentRootBox.Text : Defaults.ContentRoot,
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _contentRootBox.Text = dialog.SelectedPath;
    }

    private void SetBusy(bool busy)
    {
        _fetchButton.Enabled = !busy;
        _newTemplateButton.Enabled = !busy;
        _resolveSectionButton.Enabled = !busy && _spec is not null;
        _generateButton.Enabled = !busy && _spec is not null;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private void SetWorkflowEnabled(bool enabled)
    {
        _previousButton.Enabled = enabled;
        _resolveSectionButton.Enabled = enabled;
        _verifyButton.Enabled = enabled;
        _nextButton.Enabled = enabled;
        _generateButton.Enabled = enabled;
        _sectionList.Enabled = enabled;
    }

    private void AppendLog(string message)
    {
        if (_logBox.TextLength > 0)
            _logBox.AppendText(Environment.NewLine);
        _logBox.AppendText($"[{DateTime.Now:T}] {message}");
    }

    private static string CleanPath(string value, string fallback)
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

    private static string FormatMetadata(Dictionary<string, string> metadata)
    {
        if (metadata.Count == 0)
            return "";
        var builder = new StringBuilder();
        foreach (var pair in metadata)
        {
            if (builder.Length > 0)
                builder.Append("; ");
            builder.Append(pair.Key).Append('=').Append(pair.Value);
        }
        return builder.ToString();
    }

    private sealed record ReviewSection(
        string Key,
        string Label,
        ReviewSectionKind Kind,
        int StageIndex = -1,
        int StepIndex = -1);

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
