using System.Text.Json;
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

    private static string SettingsPath => Utilities.RuntimePath("config", "desktop-settings.json");

    public string ContentRoot { get; init; } = Defaults.ContentRoot;
    public CensusSourceKind SourceKind { get; init; } = CensusSourceKind.Daybreak;

    // Kept for compatibility with desktop-settings.json files written before
    // the Census options were split into README-aligned fields.
    public string SourceLocation { get; init; } = "";

    public string CensusServiceId { get; init; } = Defaults.CensusServiceId;
    public string CensusBaseUrl { get; init; } = Defaults.CensusBaseUrl;
    public string CensusRemoteBaseUrl { get; init; } = Defaults.CensusRemoteBaseUrl;
    public bool CensusIncludeServiceId { get; init; } = Defaults.CensusIncludeServiceId;
    public string CensusLocalDirectory { get; init; } = Defaults.CensusLocalDirectory ?? "";
    public string CensusCacheDirectory { get; init; } = Defaults.CensusCacheDirectory;
    public QuestGenerationMode GenerationMode { get; init; } = QuestGenerationMode.LegacySpawnStub;

    public bool UseDatabaseConnection { get; init; } = Defaults.HasDatabaseConfiguration;
    public bool UseDbConnectionString { get; init; } = !string.IsNullOrWhiteSpace(Defaults.DbConnectionString);
    public string DbConnectionString { get; init; } = Defaults.DbConnectionString ?? "";
    public string DbHost { get; init; } = Defaults.DbHost ?? "";
    public uint DbPort { get; init; } = Defaults.DbPort;
    public string DbName { get; init; } = Defaults.DbName ?? "";
    public string DbUser { get; init; } = Defaults.DbUser ?? "";
    public string DbPassword { get; init; } = Defaults.DbPassword ?? "";

    public bool ShowQuestSourcePanel { get; init; } = true;
    public bool ShowSettingsSummary { get; init; } = false;
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

        switch (sourceKind)
        {
            case CensusSourceKind.Daybreak:
                CensusBaseUrl = sourceLocation;
                break;
            case CensusSourceKind.Remote:
                CensusRemoteBaseUrl = sourceLocation;
                break;
            case CensusSourceKind.Local:
                CensusLocalDirectory = sourceLocation;
                break;
        }
    }

    public static QuestParserUiSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return FromEnvironment();

            var json = File.ReadAllText(SettingsPath);
            return (JsonSerializer.Deserialize(json, QuestParserDesktopJsonContext.Default.QuestParserUiSettings) ?? FromEnvironment()).Normalize();
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
            JsonSerializer.Serialize(Normalize(), QuestParserDesktopJsonContext.Default.QuestParserUiSettings),
            cancellationToken).ConfigureAwait(false);
    }

    public static QuestParserUiSettings FromEnvironment()
    {
        var census = CensusSourceOptions.FromEnvironment();
        return new QuestParserUiSettings
        {
            ContentRoot = Defaults.ContentRoot,
            SourceKind = census.Kind,
            SourceLocation = LocationFor(census.Kind, Defaults.CensusBaseUrl, Defaults.CensusRemoteBaseUrl, Defaults.CensusLocalDirectory ?? ""),
            CensusServiceId = census.ServiceId,
            CensusBaseUrl = Defaults.CensusBaseUrl,
            CensusRemoteBaseUrl = Defaults.CensusRemoteBaseUrl,
            CensusIncludeServiceId = census.IncludeServiceId,
            CensusLocalDirectory = census.LocalDirectory ?? "",
            CensusCacheDirectory = census.CacheDirectory,
            UseDatabaseConnection = Defaults.HasDatabaseConfiguration,
            UseDbConnectionString = !string.IsNullOrWhiteSpace(Defaults.DbConnectionString),
            DbConnectionString = Defaults.DbConnectionString ?? "",
            DbHost = Defaults.DbHost ?? "",
            DbPort = Defaults.DbPort,
            DbName = Defaults.DbName ?? "",
            DbUser = Defaults.DbUser ?? "",
            DbPassword = Defaults.DbPassword ?? ""
        }.Normalize();
    }

    public CensusSourceOptions ToCensusOptions()
    {
        var settings = Normalize();
        return new CensusSourceOptions
        {
            Kind = settings.SourceKind,
            BaseUrl = settings.SourceKind == CensusSourceKind.Remote
                ? settings.CensusRemoteBaseUrl
                : settings.CensusBaseUrl,
            ServiceId = settings.CensusServiceId,
            IncludeServiceId = settings.CensusIncludeServiceId,
            CacheDirectory = settings.CensusCacheDirectory,
            LocalDirectory = settings.CensusLocalDirectory
        };
    }

    public IQuestDatabaseResolver CreateDatabaseResolver()
    {
        var settings = Normalize();
        return QuestDatabaseResolverFactory.Create(
            settings.UseDatabaseConnection,
            settings.UseDbConnectionString ? settings.DbConnectionString : null,
            settings.DbHost,
            settings.DbPort,
            settings.DbName,
            settings.DbUser,
            settings.DbPassword);
    }

    public bool HasDatabaseConfiguration()
    {
        var settings = Normalize();
        if (!settings.UseDatabaseConnection)
            return false;

        return MariaDbQuestDatabaseResolver.HasDatabaseConfiguration(
            settings.UseDbConnectionString ? settings.DbConnectionString : null,
            settings.DbHost,
            settings.DbName,
            settings.DbUser);
    }

    public string BuildDatabaseConnectionString()
    {
        var settings = Normalize();
        if (!settings.UseDatabaseConnection)
            throw new InvalidOperationException("Database connection is disabled.");

        return MariaDbQuestDatabaseResolver.BuildConnectionString(
            settings.UseDbConnectionString ? settings.DbConnectionString : null,
            settings.DbHost,
            settings.DbPort,
            settings.DbName,
            settings.DbUser,
            settings.DbPassword);
    }

    public string Summary()
    {
        var settings = Normalize();
        var location = LocationFor(settings.SourceKind, settings.CensusBaseUrl, settings.CensusRemoteBaseUrl, settings.CensusLocalDirectory);
        if (string.IsNullOrWhiteSpace(location))
            location = "(not set)";

        return $"Content root: {settings.ContentRoot} | Quest source: {settings.SourceKind} | {location} | Census cache: {settings.CensusCacheDirectory} | Lua generation: {DisplayName(settings.GenerationMode)} | {settings.DatabaseSummary()}";
    }

    public string DatabaseSummary()
    {
        var settings = Normalize();
        if (!settings.UseDatabaseConnection)
            return "MariaDB disabled";

        if (!settings.HasDatabaseConfiguration())
            return "MariaDB enabled but incomplete";

        return settings.UseDbConnectionString
            ? "MariaDB configured with connection string"
            : $"MariaDB configured for {settings.DbUser}@{settings.DbHost}:{settings.DbPort}/{settings.DbName}";
    }

    public QuestParserUiSettings Normalize()
    {
        var censusBaseUrl = CleanUrl(CensusBaseUrl, Defaults.CensusBaseUrl);
        var censusRemoteBaseUrl = CleanUrl(CensusRemoteBaseUrl, Defaults.CensusRemoteBaseUrl);
        var censusLocalDirectory = CleanOptional(CensusLocalDirectory);
        var legacyLocation = CleanOptional(SourceLocation);

        if (!string.IsNullOrWhiteSpace(legacyLocation))
        {
            switch (SourceKind)
            {
                case CensusSourceKind.Daybreak:
                    censusBaseUrl = CleanUrl(legacyLocation, Defaults.CensusBaseUrl);
                    break;
                case CensusSourceKind.Remote:
                    censusRemoteBaseUrl = CleanUrl(legacyLocation, Defaults.CensusRemoteBaseUrl);
                    break;
                case CensusSourceKind.Local:
                    censusLocalDirectory = legacyLocation;
                    break;
            }
        }

        censusLocalDirectory = string.IsNullOrWhiteSpace(censusLocalDirectory)
            ? Defaults.CensusLocalDirectory ?? ""
            : censusLocalDirectory;

        return this with
        {
            ContentRoot = Clean(ContentRoot, Defaults.ContentRoot),
            SourceLocation = LocationFor(SourceKind, censusBaseUrl, censusRemoteBaseUrl, censusLocalDirectory),
            CensusServiceId = Clean(CensusServiceId, Defaults.CensusServiceId),
            CensusBaseUrl = censusBaseUrl,
            CensusRemoteBaseUrl = censusRemoteBaseUrl,
            CensusIncludeServiceId = CensusIncludeServiceId,
            CensusLocalDirectory = censusLocalDirectory,
            CensusCacheDirectory = Clean(CensusCacheDirectory, Defaults.CensusCacheDirectory),
            GenerationMode = Enum.IsDefined(GenerationMode) ? GenerationMode : QuestGenerationMode.LegacySpawnStub,
            DbConnectionString = CleanOptional(DbConnectionString),
            DbHost = CleanOptional(DbHost),
            DbPort = DbPort == 0 ? Defaults.DefaultDbPort : DbPort,
            DbName = CleanOptional(DbName),
            DbUser = CleanOptional(DbUser),
            DbPassword = DbPassword ?? "",
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

    private static string LocationFor(CensusSourceKind kind, string censusBaseUrl, string censusRemoteBaseUrl, string censusLocalDirectory)
    {
        return kind switch
        {
            CensusSourceKind.Daybreak => censusBaseUrl,
            CensusSourceKind.Remote => censusRemoteBaseUrl,
            CensusSourceKind.Local => censusLocalDirectory,
            _ => ""
        };
    }

    private static string Clean(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string CleanOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
    }

    private static string CleanUrl(string? value, string fallback)
    {
        return Clean(value, fallback).TrimEnd('/');
    }

    private static double Clamp(double value, double min, double max, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            return fallback;

        return Math.Clamp(value, min, max);
    }

    private static string DisplayName(QuestGenerationMode mode)
    {
        return mode switch
        {
            QuestGenerationMode.ModuleLua => "Quest module Lua",
            _ => "Legacy spawn stub"
        };
    }
}
