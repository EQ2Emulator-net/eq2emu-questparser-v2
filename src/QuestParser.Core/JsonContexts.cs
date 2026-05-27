using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuestParser.Core;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(CensusQuestResponse))]
[JsonSerializable(typeof(CensusQuestGiverResponse))]
public sealed partial class CensusJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(QuestSpec))]
public sealed partial class QuestSpecJsonContext : JsonSerializerContext;
