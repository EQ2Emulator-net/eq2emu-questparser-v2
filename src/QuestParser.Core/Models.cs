using System.Text.Json.Serialization;

namespace QuestParser.Core;

[JsonConverter(typeof(JsonStringEnumConverter<StepType>))]
public enum StepType
{
    Generic,
    Chat,
    Craft,
    Harvest,
    Kill,
    KillByRace,
    Location,
    ObtainItem,
    Spell,
    ZoneLocation
}

[JsonConverter(typeof(JsonStringEnumConverter<ResolveStatus>))]
public enum ResolveStatus
{
    Missing,
    Ambiguous,
    Proposed,
    Resolved
}

public sealed class QuestSpec
{
    public string SchemaVersion { get; set; } = "1.0";
    public QuestMetadata Quest { get; set; } = new();
    public OutputPaths Output { get; set; } = new();
    public Dictionary<string, string> Provenance { get; set; } = [];
    public List<string> QuestGivers { get; set; } = [];
    public ResolvedReference QuestId { get; set; } = ResolvedReference.Missing("quest", "");
    public ResolvedReference Giver { get; set; } = ResolvedReference.Missing("npc", "");
    public List<QuestStageSpec> Stages { get; set; } = [];
    public QuestRewardSpec Rewards { get; set; } = new();
    public List<string> Todos { get; set; } = [];
    public GenerationStatus Generation { get; set; } = new();
}

public sealed class QuestMetadata
{
    public string Name { get; set; } = "";
    public string Zone { get; set; } = "";
    public byte Level { get; set; }
    public byte Tier { get; set; }
    public bool Repeatable { get; set; }
    public bool Shareable { get; set; }
    public bool CompleteShareable { get; set; }
    public bool IsTradeskill { get; set; }
    public bool ScalesWithLevel { get; set; }
    public long CensusId { get; set; }
    public long CensusCrc { get; set; }
    public string StarterText { get; set; } = "";
    public string CompletionText { get; set; } = "";
    public string Author { get; set; } = "";
}

public sealed class OutputPaths
{
    public string ContentRoot { get; set; } = Defaults.ContentRoot;
    public string QuestDirectory { get; set; } = "";
    public string LuaPath { get; set; } = "";
    public string SpecPath { get; set; } = "";
    public string SqlPath { get; set; } = "";
    public string MissingReportPath { get; set; } = "";
    public string PreviewPath { get; set; } = "";
    public string SpawnScriptPath { get; set; } = "";
}

public sealed class QuestStageSpec
{
    public int Number { get; set; }
    public string Description { get; set; } = "";
    public string CompletedDescription { get; set; } = "";
    public bool IsParallel { get; set; }
    public List<QuestStepSpec> Steps { get; set; } = [];
}

public sealed class QuestStepSpec
{
    public int Number { get; set; }
    public StepType Type { get; set; }
    public string Description { get; set; } = "";
    public string CompletedDescription { get; set; } = "";
    public int QuantityMin { get; set; }
    public int QuantityMax { get; set; }
    public float Percentage { get; set; } = 100;
    public int IconId { get; set; }
    public string IconName { get; set; } = "";
    public string CompletionZone { get; set; } = "";
    public string SearchText { get; set; } = "";
    public ResolvedReference Target { get; set; } = ResolvedReference.Missing("unknown", "");
    public LocationTarget? Location { get; set; }
    public List<QuestStepOptionSpec> RandomOptions { get; set; } = [];

    [JsonIgnore]
    public bool HasRandomOptions => RandomOptions.Count > 0;
}

public sealed class QuestStepOptionSpec
{
    public string Description { get; set; } = "";
    public string CompletedDescription { get; set; } = "";
    public int QuantityMin { get; set; }
    public int QuantityMax { get; set; }
    public float Percentage { get; set; } = 100;
    public int IconId { get; set; }
    public string IconName { get; set; } = "";
    public string CompletionZone { get; set; } = "";
    public string SearchText { get; set; } = "";
    public ResolvedReference Target { get; set; } = ResolvedReference.Missing("unknown", "");
}

public sealed class LocationTarget
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Radius { get; set; } = 10;
    public ResolvedReference Zone { get; set; } = ResolvedReference.Missing("zone", "");
}

public sealed class QuestRewardSpec
{
    public int CoinMin { get; set; }
    public int CoinMax { get; set; }
    public double Experience { get; set; }
    public List<RewardItemSpec> Items { get; set; } = [];
    public List<RewardFactionSpec> Factions { get; set; } = [];
}

public sealed class RewardItemSpec
{
    public int Quantity { get; set; } = 1;
    public bool IsSelectable { get; set; }
    public ResolvedReference Item { get; set; } = ResolvedReference.Missing("item", "");
}

public sealed class RewardFactionSpec
{
    public int Amount { get; set; }
    public ResolvedReference Faction { get; set; } = ResolvedReference.Missing("faction", "");
}

public sealed class GenerationStatus
{
    public bool LuaWritten { get; set; }
    public bool SpecWritten { get; set; }
    public bool SqlWritten { get; set; }
    public bool MissingReportWritten { get; set; }
    public bool SpawnScriptWritten { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ResolvedReference
{
    public string Kind { get; set; } = "";
    public string Query { get; set; } = "";
    public ResolveStatus Status { get; set; }
    public long? Id { get; set; }
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public List<long> Ids { get; set; } = [];
    public List<ResolveCandidate> Candidates { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = [];

    public bool HasUsableId => (Status == ResolveStatus.Resolved || Status == ResolveStatus.Proposed) && Id.HasValue;

    public static ResolvedReference Missing(string kind, string query) => new()
    {
        Kind = kind,
        Query = query,
        Status = ResolveStatus.Missing,
        Source = "Unresolved"
    };

    public static ResolvedReference Proposed(string kind, string query, long id, string name = "", string source = "DB proposed value") => new()
    {
        Kind = kind,
        Query = query,
        Status = ResolveStatus.Proposed,
        Id = id,
        Name = name,
        Source = source,
        Ids = [id]
    };

    public static ResolvedReference Resolved(string kind, string query, long id, string name = "", Dictionary<string, string>? metadata = null, string source = "DB resolved value") => new()
    {
        Kind = kind,
        Query = query,
        Status = ResolveStatus.Resolved,
        Id = id,
        Name = name,
        Source = source,
        Ids = [id],
        Metadata = metadata ?? []
    };

    public static ResolvedReference Ambiguous(string kind, string query, IEnumerable<ResolveCandidate> candidates, string source = "DB candidates need review") => new()
    {
        Kind = kind,
        Query = query,
        Status = ResolveStatus.Ambiguous,
        Source = source,
        Candidates = candidates.ToList()
    };
}

public sealed class ResolveCandidate
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Zone { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Source { get; set; } = "";
    public Dictionary<string, string> Metadata { get; set; } = [];
}

internal static class ResolvedReferenceContext
{
    public static ResolvedReference Preserve(ResolvedReference original, ResolvedReference resolved)
    {
        if (string.IsNullOrWhiteSpace(resolved.Name) && !string.IsNullOrWhiteSpace(original.Name))
            resolved.Name = original.Name;

        foreach (var pair in original.Metadata)
            resolved.Metadata.TryAdd(pair.Key, pair.Value);

        if (!string.IsNullOrWhiteSpace(original.Source)
            && original.Source.StartsWith("Census ", StringComparison.OrdinalIgnoreCase)
            && !resolved.Source.Contains(original.Source, StringComparison.OrdinalIgnoreCase))
        {
            resolved.Source = string.IsNullOrWhiteSpace(resolved.Source)
                ? original.Source
                : resolved.Source + "; " + original.Source;
        }

        return resolved;
    }
}

public sealed class QuestWorkflowResult
{
    public QuestSpec Spec { get; set; } = new();
    public string Lua { get; set; } = "";
    public string SpawnScript { get; set; } = "";
    public string Sql { get; set; } = "";
    public string MissingReport { get; set; } = "";
    public List<string> WrittenFiles { get; set; } = [];
}

[JsonConverter(typeof(JsonStringEnumConverter<QuestDiagnosticSeverity>))]
public enum QuestDiagnosticSeverity
{
    Info,
    Warning,
    Blocker
}

public sealed class QuestDiagnostic
{
    public QuestDiagnosticSeverity Severity { get; set; }
    public string SectionKey { get; set; } = "";
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class MissingSpawnTemplate
{
    public string NpcName { get; set; } = "";
    public string Zone { get; set; } = "";
    public string SuggestedSpawnScriptPath { get; set; } = "";
    public string LuaTodo { get; set; } = "";
    public string CommentedSql { get; set; } = "";
    public List<string> Notes { get; set; } = [];
}

[JsonConverter(typeof(JsonStringEnumConverter<QuestTemplateKind>))]
public enum QuestTemplateKind
{
    Blank,
    SpeakToNpc,
    KillNpc,
    CollectItem,
    Harvest,
    Craft,
    VisitLocation
}

public sealed class LintResult
{
    public int LuaFiles { get; set; }
    public int TodoDbCount { get; set; }
    public int PlaceholderIdCount { get; set; }
    public int LegacyAuthorPlaceholderCount { get; set; }
    public List<string> Findings { get; set; } = [];
}
