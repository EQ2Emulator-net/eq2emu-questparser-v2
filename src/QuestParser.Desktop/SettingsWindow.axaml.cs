using Avalonia.Controls;
using Avalonia.Platform.Storage;
using QuestParser.Core;

namespace QuestParser.Desktop;

public partial class SettingsWindow : Window
{
    private readonly Dictionary<CensusSourceKind, string> _locations = new()
    {
        [CensusSourceKind.Daybreak] = QuestParserUiSettings.DefaultLocationFor(CensusSourceKind.Daybreak),
        [CensusSourceKind.Remote] = QuestParserUiSettings.DefaultLocationFor(CensusSourceKind.Remote),
        [CensusSourceKind.Local] = QuestParserUiSettings.DefaultLocationFor(CensusSourceKind.Local)
    };

    private CensusSourceKind _selectedSource;

    public SettingsWindow()
        : this(QuestParserUiSettings.Load())
    {
    }

    public SettingsWindow(QuestParserUiSettings settings)
    {
        InitializeComponent();

        CensusSourceBox.ItemsSource = Enum.GetValues<CensusSourceKind>();
        ApplySettingsToControls(settings.Normalize());

        BrowseContentRootButton.Click += async (_, _) => await BrowseFolderIntoAsync(ContentRootBox, "Choose EQ2Emu content root");
        BrowseSourceButton.Click += async (_, _) => await BrowseFolderIntoAsync(SourceLocationBox, "Choose downloaded Census JSON folder");
        CensusSourceBox.SelectionChanged += (_, _) => SourceChanged();
        SidebarWidthSlider.PropertyChanged += (_, _) => UpdateSliderLabels();
        SourcePanelHeightSlider.PropertyChanged += (_, _) => UpdateSliderLabels();
        DetailsPanelHeightSlider.PropertyChanged += (_, _) => UpdateSliderLabels();
        TextSizeSlider.PropertyChanged += (_, _) => UpdateSliderLabels();
        ResetLayoutButton.Click += (_, _) => ResetLayout();
        SaveButton.Click += (_, _) => Save();
        CancelButton.Click += (_, _) => Close(null);

        UpdateSourceHelp();
        UpdateSliderLabels();
    }

    private void ApplySettingsToControls(QuestParserUiSettings settings)
    {
        _selectedSource = settings.SourceKind;
        _locations[settings.SourceKind] = settings.SourceLocation;

        ContentRootBox.Text = settings.ContentRoot;
        CensusSourceBox.SelectedItem = settings.SourceKind;
        SourceLocationBox.Text = settings.SourceLocation;
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
    }

    private void SourceChanged()
    {
        _locations[_selectedSource] = (SourceLocationBox.Text ?? "").Trim();
        _selectedSource = CurrentSource();
        SourceLocationBox.Text = _locations.TryGetValue(_selectedSource, out var location)
            ? location
            : QuestParserUiSettings.DefaultLocationFor(_selectedSource);
        UpdateSourceHelp();
    }

    private void Save()
    {
        var settings = new QuestParserUiSettings(
            Clean(ContentRootBox.Text, Defaults.ContentRoot),
            CurrentSource(),
            (SourceLocationBox.Text ?? "").Trim())
        {
            ShowQuestSourcePanel = IsChecked(ShowQuestSourceBox),
            ShowSettingsSummary = IsChecked(ShowSettingsSummaryBox),
            ShowVerificationSteps = IsChecked(ShowVerificationStepsBox),
            ShowSourceDataPanel = IsChecked(ShowSourceDataPanelBox),
            ShowCandidatePanel = IsChecked(ShowCandidatePanelBox),
            ShowProgressPanel = IsChecked(ShowProgressPanelBox),
            SidebarWidth = SidebarWidthSlider.Value,
            SourcePanelHeight = SourcePanelHeightSlider.Value,
            DetailsPanelHeight = DetailsPanelHeightSlider.Value,
            TextSize = TextSizeSlider.Value
        }.Normalize();

        Close(settings);
    }

    private void UpdateSourceHelp()
    {
        var source = CurrentSource();
        BrowseSourceButton.IsEnabled = source == CensusSourceKind.Local;
        SourceLocationBox.PlaceholderText = source == CensusSourceKind.Local
            ? "Folder containing downloaded Census JSON"
            : "Census-compatible base URL";
        SourceHelpText.Text = source switch
        {
            CensusSourceKind.Daybreak => "Uses the official Daybreak-compatible Census endpoint.",
            CensusSourceKind.Remote => "Uses a hosted mirror with the same request and response shape as Daybreak Census.",
            CensusSourceKind.Local => "Reads already-downloaded Census-compatible quest and questgiver JSON from this folder.",
            _ => ""
        };
    }

    private CensusSourceKind CurrentSource()
    {
        return CensusSourceBox.SelectedItem is CensusSourceKind kind ? kind : CensusSourceKind.Daybreak;
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
        ShowSettingsSummaryBox.IsChecked = true;
        ShowVerificationStepsBox.IsChecked = true;
        ShowSourceDataPanelBox.IsChecked = true;
        ShowCandidatePanelBox.IsChecked = true;
        ShowProgressPanelBox.IsChecked = true;
        SidebarWidthSlider.Value = 320;
        SourcePanelHeightSlider.Value = 160;
        DetailsPanelHeightSlider.Value = 230;
        TextSizeSlider.Value = 13;
        UpdateSliderLabels();
    }

    private void UpdateSliderLabels()
    {
        SidebarWidthValueText.Text = $"{Math.Round(SidebarWidthSlider.Value):0}px";
        SourcePanelHeightValueText.Text = $"{Math.Round(SourcePanelHeightSlider.Value):0}px";
        DetailsPanelHeightValueText.Text = $"{Math.Round(DetailsPanelHeightSlider.Value):0}px";
        TextSizeValueText.Text = $"{Math.Round(TextSizeSlider.Value):0}px";
    }

    private static bool IsChecked(CheckBox checkBox)
    {
        return checkBox.IsChecked == true;
    }

    private static string Clean(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
