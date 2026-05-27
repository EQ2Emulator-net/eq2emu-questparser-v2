namespace QuestParser.Core;

public static class Defaults
{
    public const string DefaultCensusSource = "daybreak";
    public const string DefaultCensusServiceId = "s:example";
    public const string DefaultCensusBaseUrl = "https://census.daybreakgames.com";
    public const string DefaultContentRoot = "eq2emu-content";
    public const uint DefaultDbPort = 3306;

    public static string CensusSource => EnvironmentValue("EQ2QP_CENSUS_SOURCE", DefaultCensusSource);
    public static string CensusServiceId => EnvironmentValue("EQ2QP_CENSUS_SERVICE_ID", DefaultCensusServiceId);
    public static string CensusBaseUrl => EnvironmentValue("EQ2QP_CENSUS_BASE_URL", DefaultCensusBaseUrl);
    public static string CensusRemoteBaseUrl => EnvironmentValue("EQ2QP_CENSUS_REMOTE_BASE_URL", CensusBaseUrl);
    public static bool CensusIncludeServiceId => EnvironmentBool("EQ2QP_CENSUS_INCLUDE_SERVICE_ID", true);
    public static string? CensusLocalDirectory => EnvironmentValueOrNull("EQ2QP_CENSUS_LOCAL_DIR");
    public static string CensusCacheDirectory => EnvironmentValue("EQ2QP_CENSUS_CACHE_DIR", Utilities.RuntimePath("cache", "census"));
    public static string ContentRoot => EnvironmentValue("EQ2QP_CONTENT_ROOT", Path.Combine(Environment.CurrentDirectory, DefaultContentRoot));

    public static string? DbConnectionString => EnvironmentValueOrNull("EQ2QP_DB_CONNECTION");
    public static string? DbHost => EnvironmentValueOrNull("EQ2QP_DB_HOST");
    public static uint DbPort => uint.TryParse(Environment.GetEnvironmentVariable("EQ2QP_DB_PORT"), out var port) ? port : DefaultDbPort;
    public static string? DbName => EnvironmentValueOrNull("EQ2QP_DB_NAME");
    public static string? DbUser => EnvironmentValueOrNull("EQ2QP_DB_USER");
    public static string? DbPassword => EnvironmentValueOrNull("EQ2QP_DB_PASSWORD");

    public static bool HasDatabaseConfiguration =>
        !string.IsNullOrWhiteSpace(DbConnectionString)
        || (!string.IsNullOrWhiteSpace(DbHost)
            && !string.IsNullOrWhiteSpace(DbName)
            && !string.IsNullOrWhiteSpace(DbUser));

    private static string EnvironmentValue(string name, string fallback)
    {
        return EnvironmentValueOrNull(name) ?? fallback;
    }

    private static string? EnvironmentValueOrNull(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool EnvironmentBool(string name, bool fallback)
    {
        var value = EnvironmentValueOrNull(name);
        if (value is null)
            return fallback;
        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
}
