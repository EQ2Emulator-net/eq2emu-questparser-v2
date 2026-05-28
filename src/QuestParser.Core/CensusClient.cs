using System.Net;
using System.Text.Json;

namespace QuestParser.Core;

public interface ICensusClient
{
    Task<CensusQuestImport> FetchQuestAsync(string questName, CancellationToken cancellationToken = default);
}

public sealed class CensusClient : ICensusClient
{
    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly string _baseUrl;
    private readonly string _serviceId;
    private readonly bool _includeServiceId;

    public CensusClient(
        HttpClient? httpClient = null,
        string? cacheDirectory = null,
        string? baseUrl = null,
        string? serviceId = null,
        bool includeServiceId = true)
    {
        _httpClient = httpClient ?? new HttpClient();
        _cacheDirectory = cacheDirectory ?? Defaults.CensusCacheDirectory;
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? Defaults.CensusBaseUrl : baseUrl.TrimEnd('/');
        _serviceId = string.IsNullOrWhiteSpace(serviceId) ? Defaults.CensusServiceId : serviceId.Trim('/');
        _includeServiceId = includeServiceId;
    }

    public async Task<CensusQuestImport> FetchQuestAsync(string questName, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_cacheDirectory);

        var questUri = BuildQuestUri(questName, _baseUrl, _serviceId, _includeServiceId);
        var questRaw = await GetStringAsync(questUri, cancellationToken).ConfigureAwait(false);
        await WriteCachedQuestJsonAsync(_cacheDirectory, questName, questRaw, cancellationToken).ConfigureAwait(false);

        var quest = ReadQuestFromJson(questName, questRaw);

        var giverUri = BuildQuestGiverUri(quest.Id, _baseUrl, _serviceId, _includeServiceId);
        var giverRaw = await GetStringAsync(giverUri, cancellationToken).ConfigureAwait(false);
        await WriteCachedQuestGiverJsonAsync(_cacheDirectory, questName, giverRaw, cancellationToken).ConfigureAwait(false);

        return new CensusQuestImport(quest, ReadQuestGiversFromJson(giverRaw, quest.Id));
    }

    public static Uri BuildQuestUri(string questName, string? baseUrl = null, string? serviceId = null, bool includeServiceId = true)
    {
        var encoded = WebUtility.UrlEncode(questName);
        return new Uri($"{EndpointRoot(baseUrl, serviceId, includeServiceId)}/get/eq2/quest?name={encoded}&c:limit=5&c:case=false");
    }

    public static Uri BuildQuestGiverUri(long censusQuestId, string? baseUrl = null, string? serviceId = null, bool includeServiceId = true)
    {
        return new Uri($"{EndpointRoot(baseUrl, serviceId, includeServiceId)}/get/eq2/questgiver?quest_list.id={censusQuestId}&c:limit=25");
    }

    public static string QuestJsonFileName(string questName)
    {
        return $"{Utilities.CacheKey(questName)}.quest.json";
    }

    public static string QuestGiverJsonFileName(string questName)
    {
        return $"{Utilities.CacheKey(questName)}.questgivers.json";
    }

    public static CensusQuest ReadQuestFromJson(string questName, string rawJson)
    {
        using var document = JsonDocument.Parse(rawJson);
        if (!document.RootElement.TryGetProperty("quest_list", out var questList) || questList.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Census source returned an unreadable quest response.");

        JsonElement? fallback = null;
        foreach (var questElement in questList.EnumerateArray())
        {
            fallback ??= questElement.Clone();
            if (!questElement.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
                continue;

            if (string.Equals(nameElement.GetString(), questName, StringComparison.OrdinalIgnoreCase))
                return ReadQuestElement(questElement, questName);
        }

        return fallback.HasValue
            ? ReadQuestElement(fallback.Value, questName)
            : throw new InvalidOperationException($"Census source did not return quest '{questName}'.");
    }

    public static IReadOnlyList<CensusQuestGiver> ReadQuestGiversFromJson(string rawJson, long questId)
    {
        var giverResponse = JsonSerializer.Deserialize(rawJson, CensusJsonContext.Default.CensusQuestGiverResponse) ?? new();
        if (giverResponse.QuestGiverList.Any(giver => giver.QuestList.Count > 0))
            return giverResponse.QuestGiverList
                .Where(giver => giver.QuestList.Any(quest => quest.Id == questId))
                .ToList();

        return giverResponse.QuestGiverList;
    }

    public static async Task WriteCachedQuestJsonAsync(string cacheDirectory, string questName, string rawJson, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(cacheDirectory);
        await File.WriteAllTextAsync(Path.Combine(cacheDirectory, QuestJsonFileName(questName)), rawJson, cancellationToken).ConfigureAwait(false);
    }

    public static async Task WriteCachedQuestGiverJsonAsync(string cacheDirectory, string questName, string rawJson, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(cacheDirectory);
        await File.WriteAllTextAsync(Path.Combine(cacheDirectory, QuestGiverJsonFileName(questName)), rawJson, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeBaseUrl(string? baseUrl)
    {
        return (string.IsNullOrWhiteSpace(baseUrl) ? Defaults.CensusBaseUrl : baseUrl).TrimEnd('/');
    }

    private static string EndpointRoot(string? baseUrl, string? serviceId, bool includeServiceId)
    {
        var root = NormalizeBaseUrl(baseUrl);
        return includeServiceId ? $"{root}/{NormalizeServiceId(serviceId)}" : root;
    }

    private static string NormalizeServiceId(string? serviceId)
    {
        return (string.IsNullOrWhiteSpace(serviceId) ? Defaults.CensusServiceId : serviceId).Trim('/');
    }

    private static CensusQuest ReadQuestElement(JsonElement questElement, string questName)
    {
        return questElement.Deserialize(CensusJsonContext.Default.CensusQuest)
            ?? throw new InvalidOperationException($"Census source returned unreadable quest data for '{questName}'.");
    }
}

public sealed record CensusQuestImport(CensusQuest Quest, IReadOnlyList<CensusQuestGiver> QuestGivers);
