using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using QuestParser.Core;

namespace QuestParser.Desktop;

public partial class SettingsWindow : Window
{
    private static readonly GenerationModeChoice[] GenerationModes =
    [
        new(QuestGenerationMode.LegacySpawnStub, "Legacy spawn stub"),
        new(QuestGenerationMode.ModuleLua, "Quest module Lua")
    ];

    private bool _testingDatabase;
    private bool _copyingQuestModule;

    public SettingsWindow()
        : this(QuestParserUiSettings.Load())
    {
    }

    public SettingsWindow(QuestParserUiSettings settings)
    {
        InitializeComponent();

        CensusSourceBox.ItemsSource = Enum.GetValues<CensusSourceKind>();
        GenerationModeBox.ItemsSource = GenerationModes;
        ApplySettingsToControls(settings.Normalize());

        BrowseContentRootButton.Click += async (_, _) =>
        {
            await BrowseFolderIntoAsync(ContentRootBox, "Choose EQ2Emu content root");
            UpdateQuestModuleStatus();
        };
        BrowseCensusCacheButton.Click += async (_, _) => await BrowseFolderIntoAsync(CensusCacheDirectoryBox, "Choose Census cache folder");
        BrowseLocalCensusButton.Click += async (_, _) => await BrowseFolderIntoAsync(CensusLocalDirectoryBox, "Choose downloaded Census JSON folder");
        CensusSourceBox.SelectionChanged += (_, _) => UpdateSourceVisibility();
        ContentRootBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
                UpdateQuestModuleStatus();
        };
        GenerationModeBox.SelectionChanged += (_, _) => UpdateQuestModuleStatus();
        UseDatabaseConnectionBox.PropertyChanged += (_, _) => UpdateDatabaseVisibility();
        UseDbConnectionStringBox.PropertyChanged += (_, _) => UpdateDatabaseVisibility();
        TestDbConnectionButton.Click += async (_, _) => await TestDatabaseConnectionAsync();
        CopyQuestModuleButton.Click += (_, _) => CopyQuestModule();
        SidebarWidthSlider.PropertyChanged += (_, _) => UpdateSliderLabels();
        SourcePanelHeightSlider.PropertyChanged += (_, _) => UpdateSliderLabels();
        DetailsPanelHeightSlider.PropertyChanged += (_, _) => UpdateSliderLabels();
        TextSizeSlider.PropertyChanged += (_, _) => UpdateSliderLabels();
        TabTextSizeSlider.PropertyChanged += (_, _) => UpdateSliderLabels();
        DataTextSizeSlider.PropertyChanged += (_, _) => UpdateSliderLabels();
        SectionTitleTextSizeSlider.PropertyChanged += (_, _) => UpdateSliderLabels();
        ResetLayoutButton.Click += (_, _) => ResetLayout();
        SaveButton.Click += (_, _) => Save();
        CancelButton.Click += (_, _) => Close(null);

        UpdateSourceVisibility();
        UpdateDatabaseVisibility();
        UpdateQuestModuleStatus();
        UpdateSliderLabels();
    }

    private void ApplySettingsToControls(QuestParserUiSettings settings)
    {
        ContentRootBox.Text = settings.ContentRoot;
        CensusSourceBox.SelectedItem = settings.SourceKind;
        CensusServiceIdBox.Text = settings.CensusServiceId;
        CensusBaseUrlBox.Text = settings.CensusBaseUrl;
        CensusRemoteBaseUrlBox.Text = settings.CensusRemoteBaseUrl;
        CensusIncludeServiceIdBox.IsChecked = settings.CensusIncludeServiceId;
        CensusLocalDirectoryBox.Text = settings.CensusLocalDirectory;
        CensusCacheDirectoryBox.Text = settings.CensusCacheDirectory;
        GenerationModeBox.SelectedItem = GenerationModes.FirstOrDefault(choice => choice.Mode == settings.GenerationMode) ?? GenerationModes[0];
        UseDatabaseConnectionBox.IsChecked = settings.UseDatabaseConnection;
        UseDbConnectionStringBox.IsChecked = settings.UseDbConnectionString;
        DbConnectionStringBox.Text = settings.DbConnectionString;
        DbHostBox.Text = settings.DbHost;
        DbPortBox.Text = settings.DbPort.ToString(CultureInfo.InvariantCulture);
        DbNameBox.Text = settings.DbName;
        DbUserBox.Text = settings.DbUser;
        DbPasswordBox.Text = settings.DbPassword;
        ShowQuestSourceBox.IsChecked = settings.ShowQuestSourcePanel;
        ShowSettingsSummaryBox.IsChecked = settings.ShowSettingsSummary;
        ShowVerificationStepsBox.IsChecked = settings.ShowVerificationSteps;
        ShowSourceDataPanelBox.IsChecked = settings.ShowSourceDataPanel;
        ShowCandidatePanelBox.IsChecked = settings.ShowCandidatePanel;
        ShowProgressPanelBox.IsChecked = settings.ShowProgressPanel;
        SidebarWidthSlider.Value = settings.SidebarWidth;
        SourcePanelHeightSlider.Value = settings.SourcePanelHeight;
        DetailsPanelHeightSlider.Value = settings.DetailsPanelHeight;
        TextSizeSlider.Value = settings.TextSize;
        TabTextSizeSlider.Value = settings.TabTextSize;
        DataTextSizeSlider.Value = settings.DataTextSize;
        SectionTitleTextSizeSlider.Value = settings.SectionTitleTextSize;
    }

    private void Save()
    {
        Close(BuildSettingsFromControls());
    }

    private QuestParserUiSettings BuildSettingsFromControls()
    {
        var source = CurrentSource();
        return new QuestParserUiSettings
        {
            ContentRoot = Clean(ContentRootBox.Text, Defaults.ContentRoot),
            SourceKind = source,
            SourceLocation = CurrentSourceLocation(source),
            CensusServiceId = Clean(CensusServiceIdBox.Text, Defaults.CensusServiceId),
            CensusBaseUrl = Clean(CensusBaseUrlBox.Text, Defaults.CensusBaseUrl),
            CensusRemoteBaseUrl = Clean(CensusRemoteBaseUrlBox.Text, Defaults.CensusRemoteBaseUrl),
            CensusIncludeServiceId = IsChecked(CensusIncludeServiceIdBox),
            CensusLocalDirectory = CleanOptional(CensusLocalDirectoryBox.Text),
            CensusCacheDirectory = Clean(CensusCacheDirectoryBox.Text, Defaults.CensusCacheDirectory),
            GenerationMode = CurrentGenerationMode(),
            UseDatabaseConnection = IsChecked(UseDatabaseConnectionBox),
            UseDbConnectionString = IsChecked(UseDbConnectionStringBox),
            DbConnectionString = CleanOptional(DbConnectionStringBox.Text),
            DbHost = CleanOptional(DbHostBox.Text),
            DbPort = ReadDbPort(),
            DbName = CleanOptional(DbNameBox.Text),
            DbUser = CleanOptional(DbUserBox.Text),
            DbPassword = DbPasswordBox.Text ?? "",
            ShowQuestSourcePanel = IsChecked(ShowQuestSourceBox),
            ShowSettingsSummary = IsChecked(ShowSettingsSummaryBox),
            ShowVerificationSteps = IsChecked(ShowVerificationStepsBox),
            ShowSourceDataPanel = IsChecked(ShowSourceDataPanelBox),
            ShowCandidatePanel = IsChecked(ShowCandidatePanelBox),
            ShowProgressPanel = IsChecked(ShowProgressPanelBox),
            SidebarWidth = SidebarWidthSlider.Value,
            SourcePanelHeight = SourcePanelHeightSlider.Value,
            DetailsPanelHeight = DetailsPanelHeightSlider.Value,
            TextSize = TextSizeSlider.Value,
            TabTextSize = TabTextSizeSlider.Value,
            DataTextSize = DataTextSizeSlider.Value,
            SectionTitleTextSize = SectionTitleTextSizeSlider.Value
        }.Normalize();
    }

    private async Task TestDatabaseConnectionAsync()
    {
        if (_testingDatabase)
            return;

        var settings = BuildSettingsFromControls();
        if (!settings.UseDatabaseConnection)
        {
            SetDbTestStatus("Database resolution is disabled.", "#334155");
            return;
        }

        if (!settings.HasDatabaseConfiguration())
        {
            SetDbTestStatus("Database settings are incomplete.", "#9A3412");
            return;
        }

        _testingDatabase = true;
        UpdateDatabaseVisibility();
        SetDbTestStatus("Testing connection...", "#334155");

        try
        {
            await MariaDbQuestDatabaseResolver.TestConnectionAsync(settings.BuildDatabaseConnectionString());
            SetDbTestStatus("Connection succeeded.", "#166534");
        }
        catch (Exception ex)
        {
            SetDbTestStatus($"Connection failed: {ex.Message}", "#991B1B");
        }
        finally
        {
            _testingDatabase = false;
            UpdateDatabaseVisibility();
        }
    }

    private void UpdateSourceVisibility()
    {
        var source = CurrentSource();
        var remoteOrDaybreak = source is CensusSourceKind.Daybreak or CensusSourceKind.Remote;

        CensusServicePanel.IsVisible = remoteOrDaybreak;
        DaybreakCensusPanel.IsVisible = source == CensusSourceKind.Daybreak;
        RemoteCensusPanel.IsVisible = source == CensusSourceKind.Remote;
        LocalCensusPanel.IsVisible = source == CensusSourceKind.Local;

        SourceHelpText.Text = source switch
        {
            CensusSourceKind.Daybreak => "Uses the official Daybreak-compatible Census endpoint, service ID, and configured raw JSON cache.",
            CensusSourceKind.Remote => "Uses a hosted Census mirror. Disable service ID when the mirror does not include a /s:... path segment.",
            CensusSourceKind.Local => "Reads already-downloaded Census-compatible quest and questgiver JSON from the configured folder.",
            _ => ""
        };
    }

    private void UpdateDatabaseVisibility()
    {
        var enabled = IsChecked(UseDatabaseConnectionBox);
        var useConnectionString = IsChecked(UseDbConnectionStringBox);

        DatabaseSettingsPanel.IsVisible = enabled;
        DbConnectionStringPanel.IsVisible = enabled && useConnectionString;
        DbIndividualSettingsPanel.IsVisible = enabled && !useConnectionString;
        TestDbConnectionButton.IsEnabled = enabled && !_testingDatabase;
    }

    private void UpdateQuestModuleStatus()
    {
        if (CurrentGenerationMode() != QuestGenerationMode.ModuleLua)
        {
            SetQuestModuleStatus(
                "Available when Quest module Lua is selected.",
                "#64748B",
                buttonText: "Copy",
                buttonEnabled: false);
            return;
        }

        try
        {
            var contentRoot = Clean(ContentRootBox.Text, Defaults.ContentRoot);
            var status = QuestModuleDeployment.GetStatus(contentRoot);
            switch (status.State)
            {
                case QuestModuleDeploymentState.Current:
                    SetQuestModuleStatus(
                        $"Current at {QuestModuleDeployment.TargetRelativePath}.",
                        "#166534",
                        buttonText: "Current",
                        buttonEnabled: false);
                    break;
                case QuestModuleDeploymentState.Outdated:
                    SetQuestModuleStatus(
                        $"Outdated at {QuestModuleDeployment.TargetRelativePath}. Expected {status.ExpectedHash[..12]}, found {status.ActualHash?[..12]}.",
                        "#9A3412",
                        buttonText: "Update",
                        buttonEnabled: !_copyingQuestModule);
                    break;
                default:
                    SetQuestModuleStatus(
                        $"Missing at {QuestModuleDeployment.TargetRelativePath}.",
                        "#991B1B",
                        buttonText: "Copy",
                        buttonEnabled: !_copyingQuestModule);
                    break;
            }
        }
        catch (Exception ex)
        {
            SetQuestModuleStatus(
                "QuestModule status unavailable: " + ex.Message,
                "#991B1B",
                buttonText: "Copy",
                buttonEnabled: false);
        }
    }

    private void CopyQuestModule()
    {
        if (_copyingQuestModule)
            return;

        _copyingQuestModule = true;
        UpdateQuestModuleStatus();
        try
        {
            var contentRoot = Clean(ContentRootBox.Text, Defaults.ContentRoot);
            var status = QuestModuleDeployment.GetStatus(contentRoot);
            if (status.State == QuestModuleDeploymentState.Current)
                return;

            QuestModuleDeployment.CopyToContentRoot(
                contentRoot,
                overwrite: status.State == QuestModuleDeploymentState.Outdated);
        }
        catch (Exception ex)
        {
            SetQuestModuleStatus(
                "QuestModule copy failed: " + ex.Message,
                "#991B1B",
                buttonText: "Copy",
                buttonEnabled: true);
            return;
        }
        finally
        {
            _copyingQuestModule = false;
        }

        UpdateQuestModuleStatus();
    }

    private void SetQuestModuleStatus(string text, string color, string buttonText, bool buttonEnabled)
    {
        QuestModuleStatusText.Text = text;
        QuestModuleStatusText.Foreground = Brush.Parse(color);
        CopyQuestModuleButton.Content = buttonText;
        CopyQuestModuleButton.IsEnabled = buttonEnabled;
    }

    private CensusSourceKind CurrentSource()
    {
        return CensusSourceBox.SelectedItem is CensusSourceKind kind ? kind : CensusSourceKind.Daybreak;
    }

    private QuestGenerationMode CurrentGenerationMode()
    {
        return GenerationModeBox.SelectedItem is GenerationModeChoice choice
            ? choice.Mode
            : QuestGenerationMode.LegacySpawnStub;
    }

    private string CurrentSourceLocation(CensusSourceKind source)
    {
        return source switch
        {
            CensusSourceKind.Daybreak => Clean(CensusBaseUrlBox.Text, Defaults.CensusBaseUrl),
            CensusSourceKind.Remote => Clean(CensusRemoteBaseUrlBox.Text, Defaults.CensusRemoteBaseUrl),
            CensusSourceKind.Local => CleanOptional(CensusLocalDirectoryBox.Text),
            _ => ""
        };
    }

    private async Task BrowseFolderIntoAsync(TextBox target, string title)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider.CanPickFolder != true)
            return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        if (folders.Count > 0)
            target.Text = folders[0].Path.LocalPath;
    }

    private void ResetLayout()
    {
        ShowQuestSourceBox.IsChecked = true;
        ShowSettingsSummaryBox.IsChecked = false;
        ShowVerificationStepsBox.IsChecked = true;
        ShowSourceDataPanelBox.IsChecked = true;
        ShowCandidatePanelBox.IsChecked = true;
        ShowProgressPanelBox.IsChecked = true;
        SidebarWidthSlider.Value = 320;
        SourcePanelHeightSlider.Value = 160;
        DetailsPanelHeightSlider.Value = 230;
        TextSizeSlider.Value = 13;
        TabTextSizeSlider.Value = 13;
        DataTextSizeSlider.Value = 12;
        SectionTitleTextSizeSlider.Value = 16;
        UpdateSliderLabels();
    }

    private void UpdateSliderLabels()
    {
        SidebarWidthValueText.Text = $"{Math.Round(SidebarWidthSlider.Value):0}px";
        SourcePanelHeightValueText.Text = $"{Math.Round(SourcePanelHeightSlider.Value):0}px";
        DetailsPanelHeightValueText.Text = $"{Math.Round(DetailsPanelHeightSlider.Value):0}px";
        TextSizeValueText.Text = $"{Math.Round(TextSizeSlider.Value):0}px";
        TabTextSizeValueText.Text = $"{Math.Round(TabTextSizeSlider.Value):0}px";
        DataTextSizeValueText.Text = $"{Math.Round(DataTextSizeSlider.Value):0}px";
        SectionTitleTextSizeValueText.Text = $"{Math.Round(SectionTitleTextSizeSlider.Value):0}px";
    }

    private void SetDbTestStatus(string text, string color)
    {
        DbTestStatusText.Text = text;
        DbTestStatusText.Foreground = Brush.Parse(color);
    }

    private uint ReadDbPort()
    {
        return uint.TryParse(DbPortBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port > 0
            ? port
            : Defaults.DefaultDbPort;
    }

    private static bool IsChecked(CheckBox checkBox)
    {
        return checkBox.IsChecked == true;
    }

    private static string Clean(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string CleanOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
    }

    private sealed record GenerationModeChoice(QuestGenerationMode Mode, string Label)
    {
        public override string ToString() => Label;
    }
}
