using System.Text.Json;

namespace QuestParser.Core;

public sealed class QuestWorkflow
{
    private static readonly HashSet<string> StrictModuleLuaBlockerCodes = new(StringComparer.Ordinal)
    {
        "DUPLICATE_STEP_ID",
        "MODULE_LUA_STAGE_SEQUENCE",
        "STEP_QUANTITY_RANGE",
        "STEP_OPTION_QUANTITY_RANGE"
    };

    private readonly ICensusClient _censusClient;
    private readonly QuestSpecFactory _specFactory;
    private readonly IQuestDatabaseResolver _resolver;
    private readonly LuaGenerator _luaGenerator;
    private readonly ModuleLuaGenerator _moduleLuaGenerator;
    private readonly SpawnScriptGenerator _spawnScriptGenerator;
    private readonly SqlReportGenerator _sqlReportGenerator;
    private readonly QuestTemplateFactory _templateFactory = new();

    public QuestWorkflow(
        ICensusClient? censusClient = null,
        QuestSpecFactory? specFactory = null,
        IQuestDatabaseResolver? resolver = null,
        LuaGenerator? luaGenerator = null,
        SpawnScriptGenerator? spawnScriptGenerator = null,
        SqlReportGenerator? sqlReportGenerator = null,
        ModuleLuaGenerator? moduleLuaGenerator = null)
    {
        _censusClient = censusClient ?? CensusClientFactory.CreateDefault();
        _specFactory = specFactory ?? new QuestSpecFactory();
        _resolver = resolver ?? QuestDatabaseResolverFactory.CreateDefault();
        _luaGenerator = luaGenerator ?? new LuaGenerator();
        _moduleLuaGenerator = moduleLuaGenerator ?? new ModuleLuaGenerator();
        _spawnScriptGenerator = spawnScriptGenerator ?? new SpawnScriptGenerator();
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

    public Task<QuestWorkflowResult> GenerateAsync(string specPath, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        return GenerateCoreAsync(specPath, overwrite, generationMode: null, strictModuleLuaValidation: false, cancellationToken);
    }

    public Task<QuestWorkflowResult> GenerateAsync(
        string specPath,
        bool overwrite,
        CancellationToken cancellationToken,
        QuestGenerationMode? generationMode,
        bool strictModuleLuaValidation = false)
    {
        return GenerateCoreAsync(specPath, overwrite, generationMode, strictModuleLuaValidation, cancellationToken);
    }

    private async Task<QuestWorkflowResult> GenerateCoreAsync(
        string specPath,
        bool overwrite,
        QuestGenerationMode? generationMode,
        bool strictModuleLuaValidation,
        CancellationToken cancellationToken)
    {
        var spec = await ReadSpecAsync(specPath, cancellationToken).ConfigureAwait(false);
        ApplyGenerationMode(spec, generationMode);
        ThrowIfStrictModuleLuaBlockingDiagnostics(spec, overwrite, strictModuleLuaValidation);
        var preview = Preview(spec);
        var lua = preview.Lua;
        var spawnScript = preview.SpawnScript;
        var sql = preview.Sql;
        var missing = preview.MissingReport;
        var written = await WriteOutputsAsync(spec, lua, spawnScript, sql, missing, overwrite, cancellationToken).ConfigureAwait(false);
        return new QuestWorkflowResult { Spec = spec, Lua = lua, SpawnScript = spawnScript, Sql = sql, MissingReport = missing, WrittenFiles = written };
    }

    public QuestWorkflowResult Preview(QuestSpec spec)
    {
        EnsureSpawnScriptPath(spec);
        var lua = spec.GenerationMode == QuestGenerationMode.ModuleLua
            ? _moduleLuaGenerator.Generate(spec)
            : _luaGenerator.Generate(spec);
        var spawnScript = _spawnScriptGenerator.Generate(spec);
        var sql = _sqlReportGenerator.GenerateSql(spec);
        var missing = _sqlReportGenerator.GenerateMissingReport(spec);
        return new QuestWorkflowResult { Spec = spec, Lua = lua, SpawnScript = spawnScript, Sql = sql, MissingReport = missing };
    }

    public Task<QuestWorkflowResult> GenerateFromSpecAsync(QuestSpec spec, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        return GenerateFromSpecCoreAsync(spec, overwrite, generationMode: null, strictModuleLuaValidation: false, cancellationToken);
    }

    public Task<QuestWorkflowResult> GenerateFromSpecAsync(
        QuestSpec spec,
        bool overwrite,
        CancellationToken cancellationToken,
        QuestGenerationMode? generationMode,
        bool strictModuleLuaValidation = false)
    {
        return GenerateFromSpecCoreAsync(spec, overwrite, generationMode, strictModuleLuaValidation, cancellationToken);
    }

    private async Task<QuestWorkflowResult> GenerateFromSpecCoreAsync(
        QuestSpec spec,
        bool overwrite,
        QuestGenerationMode? generationMode,
        bool strictModuleLuaValidation,
        CancellationToken cancellationToken)
    {
        ApplyGenerationMode(spec, generationMode);
        ThrowIfStrictModuleLuaBlockingDiagnostics(spec, overwrite, strictModuleLuaValidation);
        var preview = Preview(spec);
        var written = await WriteOutputsAsync(spec, preview.Lua, preview.SpawnScript, preview.Sql, preview.MissingReport, overwrite, cancellationToken).ConfigureAwait(false);
        return new QuestWorkflowResult
        {
            Spec = spec,
            Lua = preview.Lua,
            SpawnScript = preview.SpawnScript,
            Sql = preview.Sql,
            MissingReport = preview.MissingReport,
            WrittenFiles = written
        };
    }

    public Task<QuestWorkflowResult> CreateAsync(string questName, string? contentRoot = null, string author = "", bool overwrite = false, CancellationToken cancellationToken = default)
    {
        return CreateCoreAsync(questName, contentRoot, author, overwrite, generationMode: null, strictModuleLuaValidation: false, cancellationToken);
    }

    public Task<QuestWorkflowResult> CreateAsync(
        string questName,
        string? contentRoot,
        string author,
        bool overwrite,
        CancellationToken cancellationToken,
        QuestGenerationMode? generationMode,
        bool strictModuleLuaValidation = false)
    {
        return CreateCoreAsync(questName, contentRoot, author, overwrite, generationMode, strictModuleLuaValidation, cancellationToken);
    }

    private async Task<QuestWorkflowResult> CreateCoreAsync(
        string questName,
        string? contentRoot,
        string author,
        bool overwrite,
        QuestGenerationMode? generationMode,
        bool strictModuleLuaValidation,
        CancellationToken cancellationToken)
    {
        var import = await _censusClient.FetchQuestAsync(questName, cancellationToken).ConfigureAwait(false);
        var spec = _specFactory.Create(import, contentRoot ?? Defaults.ContentRoot, author);
        ApplyGenerationMode(spec, generationMode);
        await _resolver.ResolveAsync(spec, cancellationToken).ConfigureAwait(false);
        ThrowIfStrictModuleLuaBlockingDiagnostics(spec, overwrite, strictModuleLuaValidation);

        var preview = Preview(spec);
        var lua = preview.Lua;
        var spawnScript = preview.SpawnScript;
        var sql = preview.Sql;
        var missing = preview.MissingReport;
        var written = await WriteOutputsAsync(spec, lua, spawnScript, sql, missing, overwrite, cancellationToken).ConfigureAwait(false);
        return new QuestWorkflowResult { Spec = spec, Lua = lua, SpawnScript = spawnScript, Sql = sql, MissingReport = missing, WrittenFiles = written };
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
            reward.Item = ResolvedReferenceContext.Preserve(
                reward.Item,
                await _resolver.ResolveReferenceAsync("item", reward.Item.Query, "", cancellationToken).ConfigureAwait(false));
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
        return await JsonSerializer.DeserializeAsync(stream, QuestSpecJsonContext.Default.QuestSpec, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Could not read quest spec '{specPath}'.");
    }

    public static async Task WriteSpecAsync(QuestSpec spec, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(spec.Output.SpecPath)!);
        spec.Generation.SpecWritten = true;
        spec.Generation.UpdatedAt = DateTimeOffset.UtcNow;
        await using var stream = File.Create(spec.Output.SpecPath);
        await JsonSerializer.SerializeAsync(stream, spec, QuestSpecJsonContext.Default.QuestSpec, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<string>> WriteOutputsAsync(QuestSpec spec, string lua, string spawnScript, string sql, string missingReport, bool overwrite, CancellationToken cancellationToken)
    {
        var written = new List<string>();
        EnsureSpawnScriptPath(spec);
        Directory.CreateDirectory(spec.Output.QuestDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(spec.Output.SpawnScriptPath)!);
        Directory.CreateDirectory(Utilities.RuntimePath("output", "preview"));
        Directory.CreateDirectory(Utilities.RuntimePath("output", "sql"));
        Directory.CreateDirectory(Utilities.RuntimePath("output", "reports"));
        Directory.CreateDirectory(Utilities.RuntimePath("logs"));

        if (File.Exists(spec.Output.LuaPath) && !overwrite)
            throw new IOException($"Lua file already exists: {spec.Output.LuaPath}. Re-run with --overwrite to replace it.");
        if (File.Exists(spec.Output.SpawnScriptPath) && !overwrite)
            throw new IOException($"Spawn script example already exists: {spec.Output.SpawnScriptPath}. Re-run with --overwrite to replace it.");

        await File.WriteAllTextAsync(spec.Output.PreviewPath, lua, cancellationToken).ConfigureAwait(false);
        written.Add(spec.Output.PreviewPath);

        await WriteSpecAsync(spec, cancellationToken).ConfigureAwait(false);
        written.Add(spec.Output.SpecPath);

        await File.WriteAllTextAsync(spec.Output.LuaPath, lua, cancellationToken).ConfigureAwait(false);
        spec.Generation.LuaWritten = true;
        written.Add(spec.Output.LuaPath);

        await File.WriteAllTextAsync(spec.Output.SpawnScriptPath, spawnScript, cancellationToken).ConfigureAwait(false);
        spec.Generation.SpawnScriptWritten = true;
        written.Add(spec.Output.SpawnScriptPath);

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

    private static void EnsureSpawnScriptPath(QuestSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Output.SpawnScriptPath))
            spec.Output.SpawnScriptPath = SpawnScriptGenerator.BuildExamplePath(spec);
    }

    private static void ApplyGenerationMode(QuestSpec spec, QuestGenerationMode? generationMode)
    {
        if (generationMode.HasValue)
            spec.GenerationMode = generationMode.Value;
    }

    private static void ThrowIfStrictModuleLuaBlockingDiagnostics(QuestSpec spec, bool overwrite, bool strictModuleLuaValidation)
    {
        if (!strictModuleLuaValidation || spec.GenerationMode != QuestGenerationMode.ModuleLua)
            return;

        var blockers = QuestSpecValidator.Validate(spec, overwrite)
            .Where(diagnostic => diagnostic.Severity == QuestDiagnosticSeverity.Blocker)
            .Where(diagnostic => StrictModuleLuaBlockerCodes.Contains(diagnostic.Code))
            .ToArray();
        if (blockers.Length == 0)
            return;

        var details = string.Join(
            Environment.NewLine,
            blockers.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
        throw new InvalidOperationException("Validation failed:" + Environment.NewLine + details);
    }

    private static void UpdateTodos(QuestSpec spec)
    {
        spec.Todos = QuestSpecValidator.Validate(spec, overwrite: true)
            .Where(diagnostic => diagnostic.Severity == QuestDiagnosticSeverity.Blocker)
            .Select(diagnostic => $"{diagnostic.SectionKey}: {diagnostic.Message}")
            .ToList();
    }
}
