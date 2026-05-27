using System.Text.Json;
using System.Text.Json.Serialization;
using QuestParser.Core;

namespace QuestParser.Desktop;

public sealed record QuestParserUiSettings
{
    public const double MinSidebarWidth = 220;
    public const double MaxSidebarWidth = 520;
    public const double MinSourcePanelHeight = 90;
    public const double MaxSourcePanelHeight = 260;
    public const double MinDetailsPanelHeight = 140;
    public const double MaxDetailsPanelHeight = 380;
    public const double MinTextSize = 12;
    public const double MaxTextSize = 18;
    public const double MinTabTextSize = 12;
    public const double MaxTabTextSize = 18;
    public const double MinDataTextSize = 11;
    public const double MaxDataTextSize = 18;
    public const double MinSectionTitleTextSize = 14;
    public const double MaxSectionTitleTextSize = 22;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string SettingsPath => Utilities.RuntimePath("config", "desktop-settings.json");

    public string ContentRoot { get; init; } = Defaults.ContentRoot;
    public CensusSourceKind SourceKind { get; init; } = CensusSourceKind.Daybreak;
    public string SourceLocation { get; init; } = Defaults.CensusBaseUrl;
    public bool ShowQuestSourcePanel { get; init; } = true;
    public bool ShowSettingsSummary { get; init; } = true;
    public bool ShowVerificationSteps { get; init; } = true;
    public bool ShowSourceDataPanel { get; init; } = true;
    public bool ShowCandidatePanel { get; init; } = true;
    public bool ShowProgressPanel { get; init; } = true;
    public double SidebarWidth { get; init; } = 320;
    public double SourcePanelHeight { get; init; } = 160;
    public double DetailsPanelHeight { get; init; } = 230;
    public double TextSize { get; init; } = 13;
    public double TabTextSize { get; init; } = 13;
    public double DataTextSize { get; init; } = 12;
    public double SectionTitleTextSize { get; init; } = 16;

    public QuestParserUiSettings()
    {
    }

    public QuestParserUiSettings(string contentRoot, CensusSourceKind sourceKind, string sourceLocation)
        : this()
    {
        ContentRoot = contentRoot;
        SourceKind = sourceKind;
        SourceLocation = sourceLocation;
    }

    public static QuestParserUiSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return FromEnvironment();

            var json = File.ReadAllText(SettingsPath);
            return (JsonSerializer.Deserialize<QuestParserUiSettings>(json, JsonOptions) ?? FromEnvironment()).Normalize();
        }
        catch
        {
            return FromEnvironment();
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        await File.WriteAllTextAsync(
            SettingsPath,
            JsonSerializer.Serialize(Normalize(), JsonOptions),
            cancellationToken).ConfigureAwait(false);
    }

    public static QuestParserUiSettings FromEnvironment()
    {
        var census = CensusSourceOptions.FromEnvironment();
        return new QuestParserUiSettings
        {
            ContentRoot = Defaults.ContentRoot,
            SourceKind = census.Kind,
            SourceLocation = LocationFor(census.Kind, census)
        };
    }

    public CensusSourceOptions ToCensusOptions()
    {
        return CensusSourceOptions.FromEnvironment().WithOverrides(
            source: SourceKind.ToString(),
            baseUrl: SourceKind == CensusSourceKind.Local ? "" : SourceLocation,
            localDirectory: SourceKind == CensusSourceKind.Local ? SourceLocation : "");
    }

    public string Summary()
    {
        var location = string.IsNullOrWhiteSpace(SourceLocation) ? "(not set)" : SourceLocation;
        return $"Content root: {ContentRoot} | Quest source: {SourceKind} | {location}";
    }

    public QuestParserUiSettings Normalize()
    {
        return this with
        {
            ContentRoot = Clean(ContentRoot, Defaults.ContentRoot),
            SourceLocation = Clean(SourceLocation, DefaultLocationFor(SourceKind)),
            SidebarWidth = Clamp(SidebarWidth, MinSidebarWidth, MaxSidebarWidth, 320),
            SourcePanelHeight = Clamp(SourcePanelHeight, MinSourcePanelHeight, MaxSourcePanelHeight, 160),
            DetailsPanelHeight = Clamp(DetailsPanelHeight, MinDetailsPanelHeight, MaxDetailsPanelHeight, 230),
            TextSize = Clamp(TextSize, MinTextSize, MaxTextSize, 13),
            TabTextSize = Clamp(TabTextSize, MinTabTextSize, MaxTabTextSize, 13),
            DataTextSize = Clamp(DataTextSize, MinDataTextSize, MaxDataTextSize, 12),
            SectionTitleTextSize = Clamp(SectionTitleTextSize, MinSectionTitleTextSize, MaxSectionTitleTextSize, 16)
        };
    }

    public static QuestParserUiSettings DefaultLayout()
    {
        return FromEnvironment();
    }

    public static string DefaultLocationFor(CensusSourceKind kind)
    {
        return kind switch
        {
            CensusSourceKind.Daybreak => Defaults.CensusBaseUrl,
            CensusSourceKind.Remote => Defaults.CensusRemoteBaseUrl,
            CensusSourceKind.Local => Defaults.CensusLocalDirectory ?? "",
            _ => ""
        };
    }

    private static string LocationFor(CensusSourceKind kind, CensusSourceOptions census)
    {
        return kind == CensusSourceKind.Local
            ? census.LocalDirectory ?? ""
            : census.BaseUrl;
    }

    private static string Clean(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static double Clamp(double value, double min, double max, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            return fallback;

        return Math.Clamp(value, min, max);
    }
}
