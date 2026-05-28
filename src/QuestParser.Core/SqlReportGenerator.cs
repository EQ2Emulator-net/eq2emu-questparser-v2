using System.Text;

namespace QuestParser.Core;

public sealed class SqlReportGenerator
{
    public string GenerateSql(QuestSpec spec)
    {
        var writer = new StringBuilder();
        writer.AppendLine("-- EQ2Emu QuestParser review SQL");
        writer.AppendLine("-- Review before applying. The tool does not execute these statements.");
        writer.AppendLine("-- Static rewards are emitted as quest_details rows; generated Lua intentionally does not add the same rewards.");
        writer.AppendLine();

        var questId = spec.QuestId.Id ?? 0;
        if (questId == 0)
        {
            writer.AppendLine("-- TODO DB: Quest ID could not be resolved or proposed.");
            WriteQuestDetailsPreview(writer, spec);
            return writer.ToString();
        }

        var luaScript = ToContentRelativePath(spec.Output.ContentRoot, spec.Output.LuaPath).Replace('\\', '/');
        var giverId = spec.Giver.Id ?? 0;
        var shareableFlag = spec.Quest.Shareable ? 1 : 0;
        var level = spec.Quest.Level;

        if (spec.QuestId.Status == ResolveStatus.Proposed)
        {
            writer.AppendLine("-- Proposed quest row");
            writer.AppendLine($"""
                INSERT INTO quests (quest_id, name, type, zone, level, enc_level, description, spawn_id, completed_text, lua_script, shareable_flag)
                VALUES ({questId}, {Utilities.SqlString(spec.Quest.Name)}, 'Solo', {Utilities.SqlString(spec.Quest.Zone)}, {level}, {level}, {Utilities.SqlString(spec.Quest.StarterText)}, {giverId}, {Utilities.SqlString(spec.Quest.CompletionText)}, {Utilities.SqlString(luaScript)}, {shareableFlag});
                """);
        }
        else
        {
            writer.AppendLine("-- Existing quest row update preview");
            writer.AppendLine($"""
                UPDATE quests
                SET description = {Utilities.SqlString(spec.Quest.StarterText)},
                    completed_text = {Utilities.SqlString(spec.Quest.CompletionText)},
                    lua_script = {Utilities.SqlString(luaScript)},
                    spawn_id = {giverId},
                    shareable_flag = {shareableFlag}
                WHERE quest_id = {questId};
                """);
        }

        WriteQuestDetails(writer, spec, questId);
        WriteSpawnScript(writer, spec);
        WriteMissingSpawnTemplates(writer, spec);
        return writer.ToString();
    }

    public string GenerateMissingReport(QuestSpec spec)
    {
        var writer = new StringBuilder();
        writer.AppendLine($"# Missing Data Report: {spec.Quest.Name}");
        writer.AppendLine();
        writer.AppendLine($"- Zone: `{spec.Quest.Zone}`");
        writer.AppendLine($"- Census quest id: `{spec.Quest.CensusId}`");
        writer.AppendLine($"- DB quest id status: `{spec.QuestId.Status}`");
        writer.AppendLine();

        WriteReferenceReport(writer, "Quest", spec.QuestId);
        WriteReferenceReport(writer, "Quest Giver", spec.Giver);

        foreach (var step in spec.Stages.SelectMany(stage => stage.Steps))
        {
            if (step.HasRandomOptions)
            {
                for (var i = 0; i < step.RandomOptions.Count; i++)
                    WriteReferenceReport(writer, $"Step {step.Number} random option {i + 1} {step.Type}", step.RandomOptions[i].Target);
            }
            else
            {
                WriteReferenceReport(writer, $"Step {step.Number} {step.Type}", step.Target);
            }
        }

        if (spec.Todos.Count > 0)
        {
            writer.AppendLine("## TODOs");
            writer.AppendLine();
            foreach (var todo in spec.Todos)
                writer.AppendLine($"- {todo}");
            writer.AppendLine();
        }

        return writer.ToString();
    }

    private static void WriteQuestDetails(StringBuilder writer, QuestSpec spec, long questId)
    {
        if (spec.Rewards.CoinMin > 0)
        {
            writer.AppendLine();
            writer.AppendLine("-- Static rewards loaded by WorldDatabase::LoadQuestDetails");
            writer.AppendLine($"""
                INSERT IGNORE INTO quest_details (quest_id, type, subtype, value, faction_id, quantity)
                VALUES ({questId}, 'Reward', 'Coin', {spec.Rewards.CoinMin}, 0, 0);
                """);
        }

        if (spec.Rewards.CoinMax > 0 && spec.Rewards.CoinMax != spec.Rewards.CoinMin)
        {
            writer.AppendLine($"""
                INSERT IGNORE INTO quest_details (quest_id, type, subtype, value, faction_id, quantity)
                VALUES ({questId}, 'Reward', 'MaxCoin', {spec.Rewards.CoinMax}, 0, 0);
                """);
        }

        if (spec.Rewards.Experience > 0)
        {
            var subtype = spec.Quest.IsTradeskill ? "TSExperience" : "Experience";
            var xp = Convert.ToInt32(Math.Round(spec.Rewards.Experience, MidpointRounding.AwayFromZero));
            writer.AppendLine($"""
                INSERT IGNORE INTO quest_details (quest_id, type, subtype, value, faction_id, quantity)
                VALUES ({questId}, 'Reward', '{subtype}', {xp}, 0, 0);
                """);
        }

        foreach (var item in spec.Rewards.Items.Where(item => item.Item.Id.HasValue))
        {
            var subtype = item.IsSelectable ? "Selectable" : "Item";
            writer.AppendLine($"""
                INSERT IGNORE INTO quest_details (quest_id, type, subtype, value, faction_id, quantity)
                VALUES ({questId}, 'Reward', '{subtype}', {item.Item.Id}, 0, {item.Quantity});
                """);
        }

        foreach (var faction in spec.Rewards.Factions.Where(faction => faction.Faction.Id.HasValue))
        {
            writer.AppendLine($"""
                INSERT IGNORE INTO quest_details (quest_id, type, subtype, value, faction_id, quantity)
                VALUES ({questId}, 'Reward', 'Faction', {faction.Amount}, {faction.Faction.Id}, 0);
                """);
        }
    }

    private static void WriteQuestDetailsPreview(StringBuilder writer, QuestSpec spec)
    {
        if (spec.Rewards.CoinMin <= 0 && spec.Rewards.CoinMax <= 0 && spec.Rewards.Experience <= 0 && spec.Rewards.Items.Count == 0 && spec.Rewards.Factions.Count == 0)
            return;

        writer.AppendLine();
        writer.AppendLine("-- Reward preview. Uncomment after choosing the correct quest_id.");
        if (spec.Rewards.CoinMin > 0)
            writer.AppendLine($"-- INSERT IGNORE INTO quest_details (quest_id, type, subtype, value, faction_id, quantity) VALUES (<quest_id>, 'Reward', 'Coin', {spec.Rewards.CoinMin}, 0, 0);");
        if (spec.Rewards.CoinMax > 0 && spec.Rewards.CoinMax != spec.Rewards.CoinMin)
            writer.AppendLine($"-- INSERT IGNORE INTO quest_details (quest_id, type, subtype, value, faction_id, quantity) VALUES (<quest_id>, 'Reward', 'MaxCoin', {spec.Rewards.CoinMax}, 0, 0);");
        if (spec.Rewards.Experience > 0)
        {
            var subtype = spec.Quest.IsTradeskill ? "TSExperience" : "Experience";
            var xp = Convert.ToInt32(Math.Round(spec.Rewards.Experience, MidpointRounding.AwayFromZero));
            writer.AppendLine($"-- INSERT IGNORE INTO quest_details (quest_id, type, subtype, value, faction_id, quantity) VALUES (<quest_id>, 'Reward', '{subtype}', {xp}, 0, 0);");
        }

        foreach (var item in spec.Rewards.Items.Where(item => item.Item.Id.HasValue))
        {
            var subtype = item.IsSelectable ? "Selectable" : "Item";
            writer.AppendLine($"-- INSERT IGNORE INTO quest_details (quest_id, type, subtype, value, faction_id, quantity) VALUES (<quest_id>, 'Reward', '{subtype}', {item.Item.Id}, 0, {item.Quantity});");
        }

        foreach (var faction in spec.Rewards.Factions.Where(faction => faction.Faction.Id.HasValue))
            writer.AppendLine($"-- INSERT IGNORE INTO quest_details (quest_id, type, subtype, value, faction_id, quantity) VALUES (<quest_id>, 'Reward', 'Faction', {faction.Amount}, {faction.Faction.Id}, 0);");
    }

    private static void WriteSpawnScript(StringBuilder writer, QuestSpec spec)
    {
        if (spec.Giver.Id is not long giverId)
            return;

        var script = Path.Combine("SpawnScripts", Utilities.SafeDirectoryName(spec.Quest.Zone), $"{Utilities.IdentifierFromName(spec.Giver.Name.Length > 0 ? spec.Giver.Name : spec.Giver.Query)}.lua").Replace('\\', '/');
        writer.AppendLine();
        writer.AppendLine("-- Optional quest giver spawn script mapping");
        writer.AppendLine($"""
            INSERT INTO spawn_scripts (spawn_id, lua_script)
            SELECT {giverId}, {Utilities.SqlString(script)}
            WHERE NOT EXISTS (SELECT 1 FROM spawn_scripts WHERE spawn_id = {giverId} AND lua_script = {Utilities.SqlString(script)});
            """);
    }

    private static void WriteMissingSpawnTemplates(StringBuilder writer, QuestSpec spec)
    {
        var missingNpcs = spec.Stages
            .SelectMany(stage => stage.Steps)
            .SelectMany(StepTargets)
            .Where(target => target.Kind == "npc" && target.Status == ResolveStatus.Missing)
            .Select(target => target.Query)
            .Where(query => !string.IsNullOrWhiteSpace(query))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (missingNpcs.Length == 0)
            return;

        writer.AppendLine();
        writer.AppendLine("-- Missing spawn templates. These are comments because model, position, faction, and placement need author review.");
        foreach (var npc in missingNpcs)
        {
            writer.AppendLine($"-- Missing NPC: {npc}");
            writer.AppendLine($"-- INSERT INTO spawn (name, targetable, show_name, command_primary, show_level) VALUES ({Utilities.SqlString(npc)}, 1, 1, 0, 1);");
            writer.AppendLine("-- INSERT INTO spawn_npcs (spawn_id, min_level, max_level) VALUES (<new_spawn_id>, <min_level>, <max_level>);");
            writer.AppendLine("-- INSERT INTO spawn_location_name (name) VALUES ('<location_name>');");
            writer.AppendLine("-- INSERT INTO spawn_location_entry (spawn_id, spawn_location_id, spawnpercentage) VALUES (<new_spawn_id>, <location_id>, 100);");
            writer.AppendLine("-- INSERT INTO spawn_location_placement (zone_id, spawn_location_id, x, y, z, heading) VALUES (<zone_id>, <location_id>, <x>, <y>, <z>, <heading>);");
        }
    }

    private static void WriteReferenceReport(StringBuilder writer, string label, ResolvedReference reference)
    {
        if (reference.Status is ResolveStatus.Resolved or ResolveStatus.Proposed)
            return;

        writer.AppendLine($"## {label}");
        writer.AppendLine();
        writer.AppendLine($"- Kind: `{reference.Kind}`");
        writer.AppendLine($"- Query: `{reference.Query}`");
        writer.AppendLine($"- Status: `{reference.Status}`");
        if (reference.Candidates.Count > 0)
        {
            writer.AppendLine("- Candidates:");
            foreach (var candidate in reference.Candidates)
                writer.AppendLine($"  - `{candidate.Id}` {candidate.Name} {candidate.Zone} {candidate.Detail}".TrimEnd());
        }
        writer.AppendLine();
    }

    private static IEnumerable<ResolvedReference> StepTargets(QuestStepSpec step)
    {
        return step.HasRandomOptions
            ? step.RandomOptions.Select(option => option.Target)
            : [step.Target];
    }

    private static string ToContentRelativePath(string contentRoot, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(contentRoot))
            return fullPath;

        var root = Path.GetFullPath(contentRoot);
        var path = Path.GetFullPath(fullPath);
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(root, path)
            : fullPath;
    }
}
