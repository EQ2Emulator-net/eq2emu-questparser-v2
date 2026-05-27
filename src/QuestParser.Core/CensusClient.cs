using System.Net;
using System.Text.Json;

namespace QuestParser.Core;

public sealed class CensusClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly string _baseUrl;
    private readonly string _serviceId;

    public CensusClient(HttpClient? httpClient = null, string? cacheDirectory = null, string? baseUrl = null, string? serviceId = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _cacheDirectory = cacheDirectory ?? Utilities.RuntimePath("cache", "census");
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? Defaults.CensusBaseUrl : baseUrl.TrimEnd('/');
        _serviceId = string.IsNullOrWhiteSpace(serviceId) ? Defaults.CensusServiceId : serviceId.Trim('/');
    }

    public async Task<CensusQuestImport> FetchQuestAsync(string questName, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_cacheDirectory);

        var questUri = BuildQuestUri(questName, _baseUrl, _serviceId);
        var questRaw = await GetStringAsync(questUri, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(_cacheDirectory, $"{Utilities.CacheKey(questName)}.quest.json"), questRaw, cancellationToken).ConfigureAwait(false);

        var questResponse = JsonSerializer.Deserialize<CensusQuestResponse>(questRaw, JsonOptions)
            ?? throw new InvalidOperationException("Census returned an unreadable quest response.");
        var quest = questResponse.QuestList.FirstOrDefault()
            ?? throw new InvalidOperationException($"Census did not return quest '{questName}'.");

        var giverUri = BuildQuestGiverUri(quest.Id, _baseUrl, _serviceId);
        var giverRaw = await GetStringAsync(giverUri, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(_cacheDirectory, $"{Utilities.CacheKey(questName)}.questgivers.json"), giverRaw, cancellationToken).ConfigureAwait(false);

        var giverResponse = JsonSerializer.Deserialize<CensusQuestGiverResponse>(giverRaw, JsonOptions) ?? new();
        return new CensusQuestImport(quest, giverResponse.QuestGiverList);
    }

    public static Uri BuildQuestUri(string questName, string? baseUrl = null, string? serviceId = null)
    {
        var encoded = WebUtility.UrlEncode(questName);
        return new Uri($"{NormalizeBaseUrl(baseUrl)}/{NormalizeServiceId(serviceId)}/get/eq2/quest?name={encoded}&c:limit=5&c:case=false");
    }

    public static Uri BuildQuestGiverUri(long censusQuestId, string? baseUrl = null, string? serviceId = null)
    {
        return new Uri($"{NormalizeBaseUrl(baseUrl)}/{NormalizeServiceId(serviceId)}/get/eq2/questgiver?quest_list.id={censusQuestId}&c:limit=25");
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

    private static string NormalizeServiceId(string? serviceId)
    {
        return (string.IsNullOrWhiteSpace(serviceId) ? Defaults.CensusServiceId : serviceId).Trim('/');
    }
}

public sealed record CensusQuestImport(CensusQuest Quest, IReadOnlyList<CensusQuestGiver> QuestGivers);
