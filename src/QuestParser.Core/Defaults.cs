namespace QuestParser.Core;

public static class Defaults
{
    public const string DefaultCensusServiceId = "s:example";
    public const string DefaultCensusBaseUrl = "https://census.daybreakgames.com";
    public const string DefaultContentRoot = "eq2emu-content";
    public const uint DefaultDbPort = 3306;

    public static string CensusServiceId => EnvironmentValue("EQ2QP_CENSUS_SERVICE_ID", DefaultCensusServiceId);
    public static string CensusBaseUrl => EnvironmentValue("EQ2QP_CENSUS_BASE_URL", DefaultCensusBaseUrl);
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
}
