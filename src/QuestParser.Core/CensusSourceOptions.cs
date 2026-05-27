namespace QuestParser.Core;

public enum CensusSourceKind
{
    Daybreak,
    Remote,
    Local
}

public sealed record CensusSourceOptions
{
    public CensusSourceKind Kind { get; init; } = CensusSourceKind.Daybreak;
    public string BaseUrl { get; init; } = Defaults.DefaultCensusBaseUrl;
    public string ServiceId { get; init; } = Defaults.DefaultCensusServiceId;
    public bool IncludeServiceId { get; init; } = true;
    public string CacheDirectory { get; init; } = Defaults.CensusCacheDirectory;
    public string? LocalDirectory { get; init; }

    public static CensusSourceOptions FromEnvironment()
    {
        var kind = ParseKind(Defaults.CensusSource);
        var baseUrl = kind == CensusSourceKind.Remote
            ? Defaults.CensusRemoteBaseUrl
            : Defaults.CensusBaseUrl;

        return new CensusSourceOptions
        {
            Kind = kind,
            BaseUrl = baseUrl,
            ServiceId = Defaults.CensusServiceId,
            IncludeServiceId = Defaults.CensusIncludeServiceId,
            CacheDirectory = Defaults.CensusCacheDirectory,
            LocalDirectory = Defaults.CensusLocalDirectory
        };
    }

    public CensusSourceOptions WithOverrides(
        string? source = null,
        string? baseUrl = null,
        string? serviceId = null,
        bool? includeServiceId = null,
        string? localDirectory = null,
        string? cacheDirectory = null)
    {
        var kind = string.IsNullOrWhiteSpace(source) ? Kind : ParseKind(source);
        return this with
        {
            Kind = kind,
            BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? BaseUrl : baseUrl.TrimEnd('/'),
            ServiceId = string.IsNullOrWhiteSpace(serviceId) ? ServiceId : serviceId.Trim('/'),
            IncludeServiceId = includeServiceId ?? IncludeServiceId,
            LocalDirectory = string.IsNullOrWhiteSpace(localDirectory) ? LocalDirectory : localDirectory.Trim(),
            CacheDirectory = string.IsNullOrWhiteSpace(cacheDirectory) ? CacheDirectory : cacheDirectory.Trim()
        };
    }

    private static CensusSourceKind ParseKind(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "daybreak" or "official" or "live" => CensusSourceKind.Daybreak,
            "remote" or "mirror" or "hosted" => CensusSourceKind.Remote,
            "local" or "offline" or "filesystem" or "file" => CensusSourceKind.Local,
            _ => throw new InvalidOperationException($"Unknown Census source '{value}'. Use daybreak, remote, or local.")
        };
    }
}
