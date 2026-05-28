using System.Text.Json;

namespace QuestParser.Core;

public sealed class LocalCensusClient : ICensusClient
{
    private readonly string _sourceDirectory;
    private readonly string _cacheDirectory;

    public LocalCensusClient(string sourceDirectory, string? cacheDirectory = null)
    {
        _sourceDirectory = sourceDirectory;
        _cacheDirectory = cacheDirectory ?? Defaults.CensusCacheDirectory;
    }

    public async Task<CensusQuestImport> FetchQuestAsync(string questName, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_sourceDirectory))
            throw new DirectoryNotFoundException($"Local Census JSON directory does not exist: {_sourceDirectory}");

        var questPath = FindRequiredFile(QuestFileCandidates(questName), "quest", questName);
        var questRaw = await File.ReadAllTextAsync(questPath, cancellationToken).ConfigureAwait(false);
        var quest = CensusClient.ReadQuestFromJson(questName, questRaw);

        var giverPath = FindRequiredFile(QuestGiverFileCandidates(questName, quest.Id), "questgiver", questName);
        var giverRaw = await File.ReadAllTextAsync(giverPath, cancellationToken).ConfigureAwait(false);
        var givers = CensusClient.ReadQuestGiversFromJson(giverRaw, quest.Id);
        var rewardItems = await ReadRewardItemsAsync(quest, cancellationToken).ConfigureAwait(false);

        await CensusClient.WriteCachedQuestJsonAsync(_cacheDirectory, questName, questRaw, cancellationToken).ConfigureAwait(false);
        await CensusClient.WriteCachedQuestGiverJsonAsync(_cacheDirectory, questName, giverRaw, cancellationToken).ConfigureAwait(false);

        return new CensusQuestImport(quest, givers, rewardItems);
    }

    private IEnumerable<string> QuestFileCandidates(string questName)
    {
        var key = Utilities.CacheKey(questName);
        yield return Path.Combine(_sourceDirectory, CensusClient.QuestJsonFileName(questName));
        yield return Path.Combine(_sourceDirectory, $"{key}.json");
        yield return Path.Combine(_sourceDirectory, "quest.json");
        yield return Path.Combine(_sourceDirectory, "quests.json");
    }

    private IEnumerable<string> QuestGiverFileCandidates(string questName, long questId)
    {
        var key = Utilities.CacheKey(questName);
        yield return Path.Combine(_sourceDirectory, CensusClient.QuestGiverJsonFileName(questName));
        yield return Path.Combine(_sourceDirectory, $"{key}.questgiver.json");
        yield return Path.Combine(_sourceDirectory, $"{questId}.questgivers.json");
        yield return Path.Combine(_sourceDirectory, $"{questId}.questgiver.json");
        yield return Path.Combine(_sourceDirectory, "questgivers.json");
        yield return Path.Combine(_sourceDirectory, "questgiver.json");
    }

    private async Task<IReadOnlyDictionary<long, CensusItem>> ReadRewardItemsAsync(CensusQuest quest, CancellationToken cancellationToken)
    {
        var ids = CensusClient.RewardItemIds(quest).ToHashSet();
        if (ids.Count == 0)
            return new Dictionary<long, CensusItem>();

        var itemPath = FindOptionalFile(ItemFileCandidates());
        if (itemPath is null)
            return new Dictionary<long, CensusItem>();

        await using var stream = File.OpenRead(itemPath);
        var response = await JsonSerializer.DeserializeAsync(stream, CensusJsonContext.Default.CensusItemResponse, cancellationToken).ConfigureAwait(false) ?? new();
        return response.ItemList
            .Where(item => ids.Contains(item.Id))
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First());
    }

    private IEnumerable<string> ItemFileCandidates()
    {
        yield return Path.Combine(_sourceDirectory, "item.json");
        yield return Path.Combine(_sourceDirectory, "items.json");
    }

    private string FindRequiredFile(IEnumerable<string> candidates, string kind, string questName)
    {
        var checkedPaths = new List<string>();
        foreach (var candidate in candidates)
        {
            checkedPaths.Add(candidate);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(
            $"Local Census source could not find {kind} JSON for '{questName}'. Checked: {string.Join(", ", checkedPaths)}");
    }

    private static string? FindOptionalFile(IEnumerable<string> candidates)
    {
        return candidates.FirstOrDefault(File.Exists);
    }
}
