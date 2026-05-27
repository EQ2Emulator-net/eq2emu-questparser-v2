using MySqlConnector;

namespace QuestParser.Core;

public interface IQuestDatabaseResolver
{
    Task ResolveAsync(QuestSpec spec, CancellationToken cancellationToken = default);

    Task<ResolvedReference> ResolveQuestIdAsync(QuestSpec spec, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("This resolver does not support per-quest DB resolution.");
    }

    Task<ResolvedReference> ResolveReferenceAsync(string kind, string query, string zone = "", CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("This resolver does not support per-reference DB resolution.");
    }

    Task<ResolvedReference> ResolveStepTargetAsync(QuestStepSpec step, string zone, CancellationToken cancellationToken = default)
    {
        return ResolveReferenceAsync(QuestSpecFactory.KindForStepType(step.Type), step.SearchText, zone, cancellationToken);
    }
}

public static class QuestDatabaseResolverFactory
{
    public static IQuestDatabaseResolver CreateDefault()
    {
        if (!Defaults.HasDatabaseConfiguration)
            return new MissingQuestDatabaseResolver("Database connection is not configured.");

        return new ResilientQuestDatabaseResolver(
            new MariaDbQuestDatabaseResolver(),
            "Database connection failed.");
    }
}

public sealed class MissingQuestDatabaseResolver : IQuestDatabaseResolver
{
    private readonly string _reason;

    public MissingQuestDatabaseResolver(string reason)
    {
        _reason = reason;
    }

    public Task ResolveAsync(QuestSpec spec, CancellationToken cancellationToken = default)
    {
        if (!HasResolvedOrProposedId(spec.QuestId))
            spec.QuestId = Missing("quest", spec.Quest.Name);

        if (!HasResolvedOrProposedId(spec.Giver) && !string.IsNullOrWhiteSpace(spec.Giver.Query))
            spec.Giver = Missing("npc", spec.Giver.Query);

        foreach (var step in spec.Stages.SelectMany(stage => stage.Steps))
        {
            if (step.HasRandomOptions)
            {
                foreach (var option in step.RandomOptions)
                {
                    if (!HasResolvedOrProposedId(option.Target))
                        option.Target = Missing(QuestSpecFactory.KindForStepType(step.Type), option.SearchText);
                }
            }
            else if (step.Type is StepType.Location or StepType.ZoneLocation)
            {
                step.Location ??= new LocationTarget();
                var zoneQuery = string.IsNullOrWhiteSpace(step.CompletionZone) ? spec.Quest.Zone : step.CompletionZone;
                if (!HasResolvedOrProposedId(step.Location.Zone))
                    step.Location.Zone = Missing("zone", zoneQuery);
            }
            else
            {
                if (!HasResolvedOrProposedId(step.Target))
                    step.Target = Missing(QuestSpecFactory.KindForStepType(step.Type), step.SearchText);
            }
        }

        foreach (var reward in spec.Rewards.Items.Where(item => !HasResolvedOrProposedId(item.Item) && !string.IsNullOrWhiteSpace(item.Item.Query)))
            reward.Item = Missing("item", reward.Item.Query);
        foreach (var reward in spec.Rewards.Factions.Where(faction => !HasResolvedOrProposedId(faction.Faction) && !string.IsNullOrWhiteSpace(faction.Faction.Query)))
            reward.Faction = Missing("faction", reward.Faction.Query);

        spec.Todos = BuildValidationTodos(spec);
        spec.Todos.Insert(0, _reason);
        return Task.CompletedTask;
    }

    public Task<ResolvedReference> ResolveQuestIdAsync(QuestSpec spec, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HasResolvedOrProposedId(spec.QuestId) ? spec.QuestId : Missing("quest", spec.Quest.Name));
    }

    public Task<ResolvedReference> ResolveReferenceAsync(string kind, string query, string zone = "", CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Missing(kind, query));
    }

    public Task<ResolvedReference> ResolveStepTargetAsync(QuestStepSpec step, string zone, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HasResolvedOrProposedId(step.Target) ? step.Target : Missing(QuestSpecFactory.KindForStepType(step.Type), step.SearchText));
    }

    private ResolvedReference Missing(string kind, string query)
    {
        var reference = ResolvedReference.Missing(kind, query);
        reference.Source = _reason;
        return reference;
    }

    private static List<string> BuildValidationTodos(QuestSpec spec)
    {
        return QuestSpecValidator.Validate(spec, overwrite: true)
            .Where(diagnostic => diagnostic.Severity == QuestDiagnosticSeverity.Blocker)
            .Select(diagnostic => $"{diagnostic.SectionKey}: {diagnostic.Message}")
            .ToList();
    }

    private static bool HasResolvedOrProposedId(ResolvedReference reference)
    {
        return reference.Status is ResolveStatus.Resolved or ResolveStatus.Proposed && reference.Id.HasValue;
    }
}

public sealed class ResilientQuestDatabaseResolver : IQuestDatabaseResolver
{
    private readonly IQuestDatabaseResolver _inner;
    private readonly string _fallbackReason;

    public ResilientQuestDatabaseResolver(IQuestDatabaseResolver inner, string fallbackReason)
    {
        _inner = inner;
        _fallbackReason = fallbackReason;
    }

    public async Task ResolveAsync(QuestSpec spec, CancellationToken cancellationToken = default)
    {
        try
        {
            await _inner.ResolveAsync(spec, cancellationToken).ConfigureAwait(false);
        }
        catch (MySqlException ex)
        {
            await new MissingQuestDatabaseResolver($"{_fallbackReason} {ex.Message}").ResolveAsync(spec, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<ResolvedReference> ResolveQuestIdAsync(QuestSpec spec, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _inner.ResolveQuestIdAsync(spec, cancellationToken).ConfigureAwait(false);
        }
        catch (MySqlException ex)
        {
            return await new MissingQuestDatabaseResolver($"{_fallbackReason} {ex.Message}").ResolveQuestIdAsync(spec, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<ResolvedReference> ResolveReferenceAsync(string kind, string query, string zone = "", CancellationToken cancellationToken = default)
    {
        try
        {
            return await _inner.ResolveReferenceAsync(kind, query, zone, cancellationToken).ConfigureAwait(false);
        }
        catch (MySqlException ex)
        {
            return await new MissingQuestDatabaseResolver($"{_fallbackReason} {ex.Message}").ResolveReferenceAsync(kind, query, zone, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<ResolvedReference> ResolveStepTargetAsync(QuestStepSpec step, string zone, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _inner.ResolveStepTargetAsync(step, zone, cancellationToken).ConfigureAwait(false);
        }
        catch (MySqlException ex)
        {
            return await new MissingQuestDatabaseResolver($"{_fallbackReason} {ex.Message}").ResolveStepTargetAsync(step, zone, cancellationToken).ConfigureAwait(false);
        }
    }
}

public sealed class MariaDbQuestDatabaseResolver : IQuestDatabaseResolver
{
    private readonly string _connectionString;

    public MariaDbQuestDatabaseResolver(string? connectionString = null)
    {
        _connectionString = connectionString ?? BuildDefaultConnectionString();
    }

    public static string BuildDefaultConnectionString()
    {
        if (!Defaults.HasDatabaseConfiguration)
            throw new InvalidOperationException("Database connection is not configured.");
        if (!string.IsNullOrWhiteSpace(Defaults.DbConnectionString))
            return Defaults.DbConnectionString;

        var builder = new MySqlConnectionStringBuilder
        {
            Server = Defaults.DbHost,
            Port = Defaults.DbPort,
            Database = Defaults.DbName,
            UserID = Defaults.DbUser,
            Password = Defaults.DbPassword,
            SslMode = MySqlSslMode.None,
            AllowUserVariables = true,
            ConnectionTimeout = 10,
            DefaultCommandTimeout = 30
        };
        return builder.ConnectionString;
    }

    public async Task ResolveAsync(QuestSpec spec, CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        spec.QuestId = await ResolveQuestAsync(connection, spec, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(spec.Giver.Query))
            spec.Giver = await ResolveNpcAsync(connection, spec.Giver.Query, spec.Quest.Zone, cancellationToken).ConfigureAwait(false);

        foreach (var stage in spec.Stages)
        {
            foreach (var step in stage.Steps)
            {
                if (step.HasRandomOptions)
                {
                    foreach (var option in step.RandomOptions)
                        option.Target = await ResolveStepTargetAsync(connection, step.Type, option.SearchText, spec.Quest.Zone, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    step.Target = await ResolveStepTargetAsync(connection, step, spec.Quest.Zone, cancellationToken).ConfigureAwait(false);
                }

                if (step.Type is StepType.Location or StepType.ZoneLocation)
                {
                    step.Location ??= new LocationTarget();
                    var zoneQuery = string.IsNullOrWhiteSpace(step.CompletionZone) ? spec.Quest.Zone : step.CompletionZone;
                    step.Location.Zone = await ResolveZoneAsync(connection, zoneQuery, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        foreach (var reward in spec.Rewards.Items.Where(item => item.Item.Status != ResolveStatus.Resolved && !string.IsNullOrWhiteSpace(item.Item.Query)))
            reward.Item = await ResolveItemAsync(connection, reward.Item.Query, cancellationToken).ConfigureAwait(false);

        foreach (var reward in spec.Rewards.Factions.Where(faction => faction.Faction.Status != ResolveStatus.Resolved && !string.IsNullOrWhiteSpace(faction.Faction.Query)))
            reward.Faction = await ResolveFactionAsync(connection, reward.Faction.Query, cancellationToken).ConfigureAwait(false);

        spec.Todos = BuildTodos(spec);
    }

    public async Task<ResolvedReference> ResolveQuestIdAsync(QuestSpec spec, CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ResolveQuestAsync(connection, spec, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ResolvedReference> ResolveReferenceAsync(string kind, string query, string zone = "", CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        return kind.ToLowerInvariant() switch
        {
            "quest" => await ResolveQuestAsync(connection, new QuestSpec { Quest = new QuestMetadata { Name = query, Zone = zone } }, cancellationToken).ConfigureAwait(false),
            "npc" => await ResolveNpcAsync(connection, query, zone, cancellationToken).ConfigureAwait(false),
            "item" => await ResolveItemAsync(connection, query, cancellationToken).ConfigureAwait(false),
            "spell" => await ResolveSpellAsync(connection, query, cancellationToken).ConfigureAwait(false),
            "faction" => await ResolveFactionAsync(connection, query, cancellationToken).ConfigureAwait(false),
            "race" => await ResolveRaceAsync(connection, query, cancellationToken).ConfigureAwait(false),
            "zone" => await ResolveZoneAsync(connection, query, cancellationToken).ConfigureAwait(false),
            "location" => ResolvedReference.Missing("location", query),
            _ => ResolvedReference.Missing(kind, query)
        };
    }

    public async Task<ResolvedReference> ResolveStepTargetAsync(QuestStepSpec step, string zone, CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ResolveStepTargetAsync(connection, step, zone, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ResolvedReference> ResolveQuestAsync(MySqlConnection connection, QuestSpec spec, CancellationToken cancellationToken)
    {
        var exact = await QueryCandidatesAsync(connection, """
            SELECT quest_id AS id, name, zone, lua_script AS detail
            FROM quests
            WHERE LOWER(name) = LOWER(@query)
              AND (@zone = '' OR LOWER(zone) = LOWER(@zone))
            ORDER BY quest_id
            LIMIT 25
            """, spec.Quest.Name, spec.Quest.Zone, "quest", cancellationToken).ConfigureAwait(false);

        var resolved = CandidateResult("quest", spec.Quest.Name, exact);
        if (resolved.Status != ResolveStatus.Missing)
            return resolved;

        var fuzzy = await QueryCandidatesAsync(connection, """
            SELECT quest_id AS id, name, zone, lua_script AS detail
            FROM quests
            WHERE name LIKE @like
            ORDER BY quest_id
            LIMIT 25
            """, spec.Quest.Name, spec.Quest.Zone, "quest", cancellationToken, fuzzy: true).ConfigureAwait(false);

        if (fuzzy.Count > 0)
            return ResolvedReference.Ambiguous("quest", spec.Quest.Name, fuzzy);

        var nextId = await QueryIntAsync(connection, "SELECT COALESCE(MAX(quest_id), 0) + 1 FROM quests", cancellationToken).ConfigureAwait(false);
        return ResolvedReference.Proposed("quest", spec.Quest.Name, nextId, spec.Quest.Name, "DB proposed MAX(quest_id)+1");
    }

    private static Task<ResolvedReference> ResolveStepTargetAsync(MySqlConnection connection, QuestStepSpec step, string zone, CancellationToken cancellationToken)
    {
        return ResolveStepTargetAsync(connection, step.Type, step.SearchText, zone, cancellationToken);
    }

    private static Task<ResolvedReference> ResolveStepTargetAsync(MySqlConnection connection, StepType stepType, string searchText, string zone, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(searchText))
            return Task.FromResult(ResolvedReference.Missing(QuestSpecFactory.KindForStepType(stepType), ""));

        return stepType switch
        {
            StepType.Chat or StepType.Kill => ResolveNpcAsync(connection, searchText, zone, cancellationToken),
            StepType.KillByRace => ResolveRaceAsync(connection, searchText, cancellationToken),
            StepType.Harvest or StepType.ObtainItem or StepType.Craft => ResolveItemAsync(connection, searchText, cancellationToken),
            StepType.Spell => ResolveSpellAsync(connection, searchText, cancellationToken),
            StepType.Location or StepType.ZoneLocation => Task.FromResult(ResolvedReference.Missing("location", searchText)),
            _ => Task.FromResult(ResolvedReference.Missing("generic", searchText))
        };
    }

    private static async Task<ResolvedReference> ResolveNpcAsync(MySqlConnection connection, string query, string zone, CancellationToken cancellationToken)
    {
        var exact = await QueryCandidatesAsync(connection, """
            SELECT DISTINCT
                s.id AS id,
                s.name AS name,
                COALESCE(z.description, z.name, '') AS zone,
                CONCAT('spawn_location=', COALESCE(sln.id, 0), '; placement=', COALESCE(slp.id, 0),
                       '; level=', COALESCE(sn.min_level, 0), '-', COALESCE(sn.max_level, 0),
                       '; xyz=', COALESCE(slp.x, 0), ',', COALESCE(slp.y, 0), ',', COALESCE(slp.z, 0)) AS detail,
                COALESCE(sln.id, 0) AS spawn_location_id,
                COALESCE(slp.id, 0) AS placement_id,
                COALESCE(z.id, 0) AS zone_id,
                COALESCE(slp.x, 0) AS x,
                COALESCE(slp.y, 0) AS y,
                COALESCE(slp.z, 0) AS z
            FROM spawn s
            LEFT JOIN spawn_npcs sn ON sn.spawn_id = s.id
            LEFT JOIN spawn_location_entry sle ON sle.spawn_id = s.id
            LEFT JOIN spawn_location_name sln ON sln.id = sle.spawn_location_id
            LEFT JOIN spawn_location_placement slp ON slp.spawn_location_id = sln.id
            LEFT JOIN zones z ON z.id = slp.zone_id
            WHERE LOWER(s.name) = LOWER(@query)
              AND (@zone = '' OR LOWER(z.description) = LOWER(@zone) OR LOWER(z.name) = LOWER(@zone) OR LOWER(z.file) = LOWER(@zone))
            ORDER BY s.id
            LIMIT 25
            """, query, zone, "npc", cancellationToken).ConfigureAwait(false);

        var resolved = CandidateResult("npc", query, exact);
        if (resolved.Status != ResolveStatus.Missing)
            return resolved;

        var fuzzy = await QueryCandidatesAsync(connection, """
            SELECT DISTINCT
                s.id AS id,
                s.name AS name,
                COALESCE(z.description, z.name, '') AS zone,
                CONCAT('spawn_location=', COALESCE(sln.id, 0), '; placement=', COALESCE(slp.id, 0),
                       '; level=', COALESCE(sn.min_level, 0), '-', COALESCE(sn.max_level, 0),
                       '; xyz=', COALESCE(slp.x, 0), ',', COALESCE(slp.y, 0), ',', COALESCE(slp.z, 0)) AS detail,
                COALESCE(sln.id, 0) AS spawn_location_id,
                COALESCE(slp.id, 0) AS placement_id,
                COALESCE(z.id, 0) AS zone_id,
                COALESCE(slp.x, 0) AS x,
                COALESCE(slp.y, 0) AS y,
                COALESCE(slp.z, 0) AS z
            FROM spawn s
            LEFT JOIN spawn_npcs sn ON sn.spawn_id = s.id
            LEFT JOIN spawn_location_entry sle ON sle.spawn_id = s.id
            LEFT JOIN spawn_location_name sln ON sln.id = sle.spawn_location_id
            LEFT JOIN spawn_location_placement slp ON slp.spawn_location_id = sln.id
            LEFT JOIN zones z ON z.id = slp.zone_id
            WHERE s.name LIKE @like
            ORDER BY s.id
            LIMIT 25
            """, query, zone, "npc", cancellationToken, fuzzy: true).ConfigureAwait(false);

        var fuzzyResult = CandidateResult("npc", query, fuzzy);
        if (fuzzyResult.Status != ResolveStatus.Missing)
            return fuzzyResult;

        foreach (var alternate in AlternateQueries(query))
        {
            var alternateMatches = await QueryCandidatesAsync(connection, """
                SELECT DISTINCT
                    s.id AS id,
                    s.name AS name,
                    COALESCE(z.description, z.name, '') AS zone,
                    CONCAT('spawn_location=', COALESCE(sln.id, 0), '; placement=', COALESCE(slp.id, 0),
                           '; level=', COALESCE(sn.min_level, 0), '-', COALESCE(sn.max_level, 0),
                           '; xyz=', COALESCE(slp.x, 0), ',', COALESCE(slp.y, 0), ',', COALESCE(slp.z, 0)) AS detail,
                    COALESCE(sln.id, 0) AS spawn_location_id,
                    COALESCE(slp.id, 0) AS placement_id,
                    COALESCE(z.id, 0) AS zone_id,
                    COALESCE(slp.x, 0) AS x,
                    COALESCE(slp.y, 0) AS y,
                    COALESCE(slp.z, 0) AS z
                FROM spawn s
                LEFT JOIN spawn_npcs sn ON sn.spawn_id = s.id
                LEFT JOIN spawn_location_entry sle ON sle.spawn_id = s.id
                LEFT JOIN spawn_location_name sln ON sln.id = sle.spawn_location_id
                LEFT JOIN spawn_location_placement slp ON slp.spawn_location_id = sln.id
                LEFT JOIN zones z ON z.id = slp.zone_id
                WHERE s.name LIKE @like
                ORDER BY s.id
                LIMIT 25
                """, alternate, zone, "npc", cancellationToken, fuzzy: true).ConfigureAwait(false);
            var alternateResult = CandidateResult("npc", query, alternateMatches);
            if (alternateResult.Status != ResolveStatus.Missing)
                return alternateResult;
        }

        return fuzzyResult;
    }

    private static Task<ResolvedReference> ResolveItemAsync(MySqlConnection connection, string query, CancellationToken cancellationToken)
    {
        return ResolveSimpleAsync(connection, "item", query, "items", "id", "name", "item_type", cancellationToken);
    }

    private static Task<ResolvedReference> ResolveSpellAsync(MySqlConnection connection, string query, CancellationToken cancellationToken)
    {
        return ResolveSimpleAsync(connection, "spell", query, "spells", "id", "name", "description", cancellationToken);
    }

    private static Task<ResolvedReference> ResolveFactionAsync(MySqlConnection connection, string query, CancellationToken cancellationToken)
    {
        return ResolveSimpleAsync(connection, "faction", query, "factions", "id", "name", "description", cancellationToken);
    }

    private static Task<ResolvedReference> ResolveRaceAsync(MySqlConnection connection, string query, CancellationToken cancellationToken)
    {
        return ResolveSimpleAsync(connection, "race", query, "eq2races", "id", "name", "name", cancellationToken);
    }

    private static async Task<ResolvedReference> ResolveZoneAsync(MySqlConnection connection, string query, CancellationToken cancellationToken)
    {
        var cleaned = query.Replace("zones/", "", StringComparison.OrdinalIgnoreCase).Trim();
        var candidates = await QueryCandidatesAsync(connection, """
            SELECT id, description AS name, name AS zone, file AS detail
            FROM zones
            WHERE LOWER(name) = LOWER(@query)
               OR LOWER(description) = LOWER(@query)
               OR LOWER(file) = LOWER(@query)
            ORDER BY id
            LIMIT 25
            """, cleaned, "", "zone", cancellationToken).ConfigureAwait(false);

        var resolved = CandidateResult("zone", query, candidates);
        if (resolved.Status != ResolveStatus.Missing)
            return resolved;

        candidates = await QueryCandidatesAsync(connection, """
            SELECT id, description AS name, name AS zone, file AS detail
            FROM zones
            WHERE name LIKE @like OR description LIKE @like OR file LIKE @like
            ORDER BY id
            LIMIT 25
            """, cleaned, "", "zone", cancellationToken, fuzzy: true).ConfigureAwait(false);
        return CandidateResult("zone", query, candidates);
    }

    private static async Task<ResolvedReference> ResolveSimpleAsync(
        MySqlConnection connection,
        string kind,
        string query,
        string table,
        string idColumn,
        string nameColumn,
        string detailColumn,
        CancellationToken cancellationToken)
    {
        var exactSql = $"""
            SELECT {idColumn} AS id, {nameColumn} AS name, '' AS zone, {detailColumn} AS detail
            FROM {table}
            WHERE LOWER({nameColumn}) = LOWER(@query)
            ORDER BY {idColumn}
            LIMIT 25
            """;

        var exact = await QueryCandidatesAsync(connection, exactSql, query, "", kind, cancellationToken).ConfigureAwait(false);
        var resolved = CandidateResult(kind, query, exact);
        if (resolved.Status != ResolveStatus.Missing)
            return resolved;

        var fuzzySql = $"""
            SELECT {idColumn} AS id, {nameColumn} AS name, '' AS zone, {detailColumn} AS detail
            FROM {table}
            WHERE {nameColumn} LIKE @like
            ORDER BY {idColumn}
            LIMIT 25
            """;
        var fuzzy = await QueryCandidatesAsync(connection, fuzzySql, query, "", kind, cancellationToken, fuzzy: true).ConfigureAwait(false);
        var fuzzyResult = CandidateResult(kind, query, fuzzy);
        if (fuzzyResult.Status != ResolveStatus.Missing)
            return fuzzyResult;

        foreach (var alternate in AlternateQueries(query))
        {
            var alternateMatches = await QueryCandidatesAsync(connection, fuzzySql, alternate, "", kind, cancellationToken, fuzzy: true).ConfigureAwait(false);
            var alternateResult = CandidateResult(kind, query, alternateMatches);
            if (alternateResult.Status != ResolveStatus.Missing)
                return alternateResult;
        }

        return fuzzyResult;
    }

    private static async Task<List<ResolveCandidate>> QueryCandidatesAsync(
        MySqlConnection connection,
        string sql,
        string query,
        string zone,
        string kind,
        CancellationToken cancellationToken,
        bool fuzzy = false)
    {
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@query", query);
        command.Parameters.AddWithValue("@zone", zone);
        command.Parameters.AddWithValue("@like", $"%{query}%");

        var candidates = new Dictionary<int, ResolveCandidate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = reader.GetInt32(reader.GetOrdinal("id"));
            if (!candidates.TryGetValue(id, out var candidate))
            {
                candidate = new ResolveCandidate
                {
                    Id = id,
                    Kind = kind,
                    Name = GetString(reader, "name"),
                    Zone = GetString(reader, "zone"),
                    Detail = GetString(reader, "detail"),
                    Source = fuzzy ? "DB fuzzy match" : "DB exact match"
                };
                candidates.Add(id, candidate);
            }

            foreach (var field in new[] { "spawn_location_id", "placement_id", "zone_id", "x", "y", "z" })
            {
                if (HasColumn(reader, field))
                    candidate.Metadata[field] = Convert.ToString(reader[field], System.Globalization.CultureInfo.InvariantCulture) ?? "";
            }
        }

        return candidates.Values
            .OrderBy(c => fuzzy ? LevenshteinDistance(query.ToLowerInvariant(), c.Name.ToLowerInvariant()) : 0)
            .ThenBy(c => c.Id)
            .ToList();
    }

    private static async Task<int> QueryIntAsync(MySqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static ResolvedReference CandidateResult(string kind, string query, IReadOnlyList<ResolveCandidate> candidates)
    {
        if (candidates.Count == 0)
            return new ResolvedReference
            {
                Kind = kind,
                Query = query,
                Status = ResolveStatus.Missing,
                Source = "DB search returned no matches"
            };

        if (candidates.Count == 1)
        {
            var candidate = candidates[0];
            return ResolvedReference.Resolved(kind, query, candidate.Id, candidate.Name, candidate.Metadata, string.IsNullOrWhiteSpace(candidate.Source) ? "DB exact match" : candidate.Source);
        }

        return ResolvedReference.Ambiguous(kind, query, candidates, "DB search returned multiple candidates");
    }

    private static string GetString(MySqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? "" : Convert.ToString(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }

    private static bool HasColumn(MySqlDataReader reader, string name)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static List<string> BuildTodos(QuestSpec spec)
    {
        var todos = new List<string>();
        AddIfNotResolved(todos, "Quest DB id", spec.QuestId);
        AddIfNotResolved(todos, "Quest giver", spec.Giver);

        foreach (var step in spec.Stages.SelectMany(stage => stage.Steps))
        {
            if (step.Type is StepType.Location or StepType.ZoneLocation)
            {
                todos.Add($"Step {step.Number}: enter coordinates for location step '{step.Description}'.");
                AddIfNotResolved(todos, $"Step {step.Number} zone", step.Location?.Zone);
            }
            else if (step.HasRandomOptions)
            {
                for (var i = 0; i < step.RandomOptions.Count; i++)
                    AddIfNotResolved(todos, $"Step {step.Number} random option {i + 1} target", step.RandomOptions[i].Target);
            }
            else
            {
                AddIfNotResolved(todos, $"Step {step.Number} target", step.Target);
            }
        }

        return todos;
    }

    private static void AddIfNotResolved(List<string> todos, string label, ResolvedReference? reference)
    {
        if (reference is null)
            return;
        if (reference.Status == ResolveStatus.Resolved || reference.Status == ResolveStatus.Proposed)
            return;

        todos.Add($"{label}: {reference.Status.ToString().ToLowerInvariant()} '{reference.Query}'.");
    }

    private static int LevenshteinDistance(string left, string right)
    {
        if (left.Length == 0) return right.Length;
        if (right.Length == 0) return left.Length;

        var costs = new int[right.Length + 1];
        for (var j = 0; j <= right.Length; j++)
            costs[j] = j;

        for (var i = 1; i <= left.Length; i++)
        {
            costs[0] = i;
            var corner = i - 1;
            for (var j = 1; j <= right.Length; j++)
            {
                var upper = costs[j];
                costs[j] = left[i - 1] == right[j - 1]
                    ? corner
                    : Math.Min(Math.Min(costs[j - 1], upper), corner) + 1;
                corner = upper;
            }
        }

        return costs[right.Length];
    }

    private static IEnumerable<string> AlternateQueries(string query)
    {
        var trimmed = query.Trim();
        var alternates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var prefix in new[] { "a ", "an ", "the ", "piece of ", "pieces of " })
        {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                alternates.Add(trimmed[prefix.Length..].Trim());
        }

        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length > 0 && words[^1].EndsWith('s') && words[^1].Length > 3)
        {
            words[^1] = words[^1][..^1];
            alternates.Add(string.Join(' ', words));
        }

        return alternates.Where(value => value.Length > 0 && !string.Equals(value, query, StringComparison.OrdinalIgnoreCase));
    }
}
