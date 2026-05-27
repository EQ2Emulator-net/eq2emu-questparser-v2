using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuestParser.Core;

public sealed class QuestWorkflow
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly CensusClient _censusClient;
    private readonly QuestSpecFactory _specFactory;
    private readonly IQuestDatabaseResolver _resolver;
    private readonly LuaGenerator _luaGenerator;
    private readonly SqlReportGenerator _sqlReportGenerator;
    private readonly QuestTemplateFactory _templateFactory = new();

    public QuestWorkflow(
        CensusClient? censusClient = null,
        QuestSpecFactory? specFactory = null,
        IQuestDatabaseResolver? resolver = null,
        LuaGenerator? luaGenerator = null,
        SqlReportGenerator? sqlReportGenerator = null)
    {
        _censusClient = censusClient ?? new CensusClient();
        _specFactory = specFactory ?? new QuestSpecFactory();
        _resolver = resolver ?? QuestDatabaseResolverFactory.CreateDefault();
        _luaGenerator = luaGenerator ?? new LuaGenerator();
        _sqlReportGenerator = sqlReportGenerator ?? new SqlReportGenerator();
    }

    public async Task<QuestWorkflowResult> ImportAsync(string questName, string? contentRoot = null, string author = "", CancellationToken cancellationToken = default)
    {
        var import = await _censusClient.FetchQuestAsync(questName, cancellationToken).ConfigureAwait(false);
        var spec = _specFactory.Create(import, contentRoot ?? Defaults.ContentRoot, author);
        await WriteSpecAsync(spec, cancellationToken).ConfigureAwait(false);
        return new QuestWorkflowResult { Spec = spec, WrittenFiles = [spec.Output.SpecPath] };
    }

    public QuestWorkflowResult CreateTemplate(QuestTemplateKind kind, string questName, string zone, string? contentRoot = null, string author = "")
    {
        var spec = _templateFactory.Create(kind, questName, zone, contentRoot ?? Defaults.ContentRoot, author);
        return Preview(spec);
    }

    public async Task<QuestWorkflowResult> ResolveAsync(string specPath, CancellationToken cancellationToken = default)
    {
        var spec = await ReadSpecAsync(specPath, cancellationToken).ConfigureAwait(false);
        await _resolver.ResolveAsync(spec, cancellationToken).ConfigureAwait(false);
        await WriteSpecAsync(spec, cancellationToken).ConfigureAwait(false);
        return new QuestWorkflowResult { Spec = spec, WrittenFiles = [spec.Output.SpecPath] };
    }

    public async Task<QuestWorkflowResult> GenerateAsync(string specPath, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        var spec = await ReadSpecAsync(specPath, cancellationToken).ConfigureAwait(false);
        var preview = Preview(spec);
        var lua = preview.Lua;
        var sql = preview.Sql;
        var missing = preview.MissingReport;
        var written = await WriteOutputsAsync(spec, lua, sql, missing, overwrite, cancellationToken).ConfigureAwait(false);
        return new QuestWorkflowResult { Spec = spec, Lua = lua, Sql = sql, MissingReport = missing, WrittenFiles = written };
    }

    public QuestWorkflowResult Preview(QuestSpec spec)
    {
        var lua = _luaGenerator.Generate(spec);
        var sql = _sqlReportGenerator.GenerateSql(spec);
        var missing = _sqlReportGenerator.GenerateMissingReport(spec);
        return new QuestWorkflowResult { Spec = spec, Lua = lua, Sql = sql, MissingReport = missing };
    }

    public async Task<QuestWorkflowResult> GenerateFromSpecAsync(QuestSpec spec, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        var preview = Preview(spec);
        var written = await WriteOutputsAsync(spec, preview.Lua, preview.Sql, preview.MissingReport, overwrite, cancellationToken).ConfigureAwait(false);
        return new QuestWorkflowResult
        {
            Spec = spec,
            Lua = preview.Lua,
            Sql = preview.Sql,
            MissingReport = preview.MissingReport,
            WrittenFiles = written
        };
    }

    public async Task<QuestWorkflowResult> CreateAsync(string questName, string? contentRoot = null, string author = "", bool overwrite = false, CancellationToken cancellationToken = default)
    {
        var import = await _censusClient.FetchQuestAsync(questName, cancellationToken).ConfigureAwait(false);
        var spec = _specFactory.Create(import, contentRoot ?? Defaults.ContentRoot, author);
        await _resolver.ResolveAsync(spec, cancellationToken).ConfigureAwait(false);

        var preview = Preview(spec);
        var lua = preview.Lua;
        var sql = preview.Sql;
        var missing = preview.MissingReport;
        var written = await WriteOutputsAsync(spec, lua, sql, missing, overwrite, cancellationToken).ConfigureAwait(false);
        return new QuestWorkflowResult { Spec = spec, Lua = lua, Sql = sql, MissingReport = missing, WrittenFiles = written };
    }

    public async Task<ResolvedReference> ResolveQuestIdAsync(QuestSpec spec, CancellationToken cancellationToken = default)
    {
        spec.QuestId = await _resolver.ResolveQuestIdAsync(spec, cancellationToken).ConfigureAwait(false);
        spec.Provenance["questId"] = spec.QuestId.Source;
        UpdateTodos(spec);
        return spec.QuestId;
    }

    public async Task<ResolvedReference> ResolveGiverAsync(QuestSpec spec, CancellationToken cancellationToken = default)
    {
        spec.Giver = await _resolver.ResolveReferenceAsync("npc", spec.Giver.Query, spec.Quest.Zone, cancellationToken).ConfigureAwait(false);
        spec.Provenance["giver"] = spec.Giver.Source;
        UpdateTodos(spec);
        return spec.Giver;
    }

    public async Task<ResolvedReference> ResolveReferenceAsync(string kind, string query, string zone = "", CancellationToken cancellationToken = default)
    {
        return await _resolver.ResolveReferenceAsync(kind, query, zone, cancellationToken).ConfigureAwait(false);
    }

    public async Task ResolveStepAsync(QuestSpec spec, int stageIndex, int stepIndex, CancellationToken cancellationToken = default)
    {
        var step = spec.Stages[stageIndex].Steps[stepIndex];
        if (step.HasRandomOptions)
        {
            var kind = QuestSpecFactory.KindForStepType(step.Type);
            foreach (var option in step.RandomOptions)
                option.Target = await _resolver.ResolveReferenceAsync(kind, option.SearchText, spec.Quest.Zone, cancellationToken).ConfigureAwait(false);
            spec.Provenance[$"step.{step.Number}.randomOptions"] = "DB resolved random options";
        }
        else
        {
            step.Target = await _resolver.ResolveStepTargetAsync(step, spec.Quest.Zone, cancellationToken).ConfigureAwait(false);
            spec.Provenance[$"step.{step.Number}.target"] = step.Target.Source;
        }

        if (step.Type is StepType.Location or StepType.ZoneLocation)
        {
            step.Location ??= new LocationTarget();
            var zoneQuery = string.IsNullOrWhiteSpace(step.CompletionZone) ? spec.Quest.Zone : step.CompletionZone;
            step.Location.Zone = await _resolver.ResolveReferenceAsync("zone", zoneQuery, "", cancellationToken).ConfigureAwait(false);
            spec.Provenance[$"step.{step.Number}.location.zone"] = step.Location.Zone.Source;
        }

        UpdateTodos(spec);
    }

    public async Task ResolveRewardsAsync(QuestSpec spec, CancellationToken cancellationToken = default)
    {
        foreach (var reward in spec.Rewards.Items.Where(item => !string.IsNullOrWhiteSpace(item.Item.Query)))
        {
            reward.Item = await _resolver.ResolveReferenceAsync("item", reward.Item.Query, "", cancellationToken).ConfigureAwait(false);
            spec.Provenance["rewards.items"] = reward.Item.Source;
        }

        foreach (var reward in spec.Rewards.Factions.Where(faction => !string.IsNullOrWhiteSpace(faction.Faction.Query)))
        {
            reward.Faction = await _resolver.ResolveReferenceAsync("faction", reward.Faction.Query, "", cancellationToken).ConfigureAwait(false);
            spec.Provenance["rewards.factions"] = reward.Faction.Source;
        }

        UpdateTodos(spec);
    }

    public static async Task<QuestSpec> ReadSpecAsync(string specPath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(specPath);
        return await JsonSerializer.DeserializeAsync<QuestSpec>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Could not read quest spec '{specPath}'.");
    }

    public static async Task WriteSpecAsync(QuestSpec spec, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(spec.Output.SpecPath)!);
        spec.Generation.SpecWritten = true;
        spec.Generation.UpdatedAt = DateTimeOffset.UtcNow;
        await using var stream = File.Create(spec.Output.SpecPath);
        await JsonSerializer.SerializeAsync(stream, spec, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<string>> WriteOutputsAsync(QuestSpec spec, string lua, string sql, string missingReport, bool overwrite, CancellationToken cancellationToken)
    {
        var written = new List<string>();
        Directory.CreateDirectory(spec.Output.QuestDirectory);
        Directory.CreateDirectory(Utilities.RuntimePath("output", "preview"));
        Directory.CreateDirectory(Utilities.RuntimePath("output", "sql"));
        Directory.CreateDirectory(Utilities.RuntimePath("output", "reports"));
        Directory.CreateDirectory(Utilities.RuntimePath("logs"));

        if (File.Exists(spec.Output.LuaPath) && !overwrite)
            throw new IOException($"Lua file already exists: {spec.Output.LuaPath}. Re-run with --overwrite to replace it.");

        await File.WriteAllTextAsync(spec.Output.PreviewPath, lua, cancellationToken).ConfigureAwait(false);
        written.Add(spec.Output.PreviewPath);

        await WriteSpecAsync(spec, cancellationToken).ConfigureAwait(false);
        written.Add(spec.Output.SpecPath);

        await File.WriteAllTextAsync(spec.Output.LuaPath, lua, cancellationToken).ConfigureAwait(false);
        spec.Generation.LuaWritten = true;
        written.Add(spec.Output.LuaPath);

        await File.WriteAllTextAsync(spec.Output.SqlPath, sql, cancellationToken).ConfigureAwait(false);
        spec.Generation.SqlWritten = true;
        written.Add(spec.Output.SqlPath);
        await File.WriteAllTextAsync(Path.Combine(Utilities.RuntimePath("output", "sql"), Path.GetFileName(spec.Output.SqlPath)), sql, cancellationToken).ConfigureAwait(false);

        await File.WriteAllTextAsync(spec.Output.MissingReportPath, missingReport, cancellationToken).ConfigureAwait(false);
        spec.Generation.MissingReportWritten = true;
        written.Add(spec.Output.MissingReportPath);
        await File.WriteAllTextAsync(Path.Combine(Utilities.RuntimePath("output", "reports"), Path.GetFileName(spec.Output.MissingReportPath)), missingReport, cancellationToken).ConfigureAwait(false);

        await WriteSpecAsync(spec, cancellationToken).ConfigureAwait(false);
        return written;
    }

    private static void UpdateTodos(QuestSpec spec)
    {
        spec.Todos = QuestSpecValidator.Validate(spec, overwrite: true)
            .Where(diagnostic => diagnostic.Severity == QuestDiagnosticSeverity.Blocker)
            .Select(diagnostic => $"{diagnostic.SectionKey}: {diagnostic.Message}")
            .ToList();
    }
}
