using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuestParser.Desktop;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(QuestParserUiSettings))]
internal sealed partial class QuestParserDesktopJsonContext : JsonSerializerContext;
