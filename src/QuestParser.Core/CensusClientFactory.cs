namespace QuestParser.Core;

public static class CensusClientFactory
{
    public static ICensusClient CreateDefault(HttpClient? httpClient = null)
    {
        return Create(CensusSourceOptions.FromEnvironment(), httpClient);
    }

    public static ICensusClient Create(CensusSourceOptions options, HttpClient? httpClient = null)
    {
        return options.Kind switch
        {
            CensusSourceKind.Local => CreateLocal(options),
            CensusSourceKind.Daybreak or CensusSourceKind.Remote => new CensusClient(
                httpClient,
                options.CacheDirectory,
                options.BaseUrl,
                options.ServiceId,
                options.IncludeServiceId),
            _ => throw new InvalidOperationException($"Unsupported Census source '{options.Kind}'.")
        };
    }

    private static LocalCensusClient CreateLocal(CensusSourceOptions options)
    {
        return new LocalCensusClient(options.LocalDirectory ?? "", options.CacheDirectory);
    }
}
