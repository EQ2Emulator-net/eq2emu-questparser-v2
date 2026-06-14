using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuestParser.Core;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(CensusQuestResponse))]
[JsonSerializable(typeof(CensusQuestGiverResponse))]
[JsonSerializable(typeof(CensusItemResponse))]
[JsonSerializable(typeof(CensusQuest))]
[JsonSerializable(typeof(CensusQuestGiver))]
[JsonSerializable(typeof(CensusItem))]
public sealed partial class CensusJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(QuestSpec))]
[JsonSerializable(typeof(QuestVisualEditorState))]
[JsonSerializable(typeof(QuestGraphNodeLayout))]
[JsonSerializable(typeof(QuestGraphViewport))]
public sealed partial class QuestSpecJsonContext : JsonSerializerContext;
