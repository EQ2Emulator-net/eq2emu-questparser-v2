using System.Text;

namespace QuestParser.Core;

public static class MissingSpawnTemplateBuilder
{
    public static MissingSpawnTemplate Build(QuestSpec spec, ResolvedReference reference, string context)
    {
        var npcName = FirstNonBlank(reference.Query, reference.Name, "TODO npc");
        var zone = string.IsNullOrWhiteSpace(spec.Quest.Zone) ? "TODO zone" : spec.Quest.Zone;
        var scriptName = Utilities.IdentifierFromName(npcName);
        var safeZone = Utilities.SafeDirectoryName(zone);
        var spawnScriptPath = Path.Combine("SpawnScripts", safeZone, $"{scriptName}.lua").Replace('\\', '/');

        return new MissingSpawnTemplate
        {
            NpcName = npcName,
            Zone = zone,
            SuggestedSpawnScriptPath = spawnScriptPath,
            LuaTodo = $"-- TODO DB: Create or resolve spawn for {npcName} before this quest is loaded.",
            CommentedSql = BuildSql(npcName, zone, spawnScriptPath),
            Notes =
            [
                $"Context: {context}",
                "These statements are comments by design. Pick model, level range, faction, position, heading, and respawn details before applying anything.",
                "After the spawn exists, enter the spawn id in the current DB reference or press Resolve Section again."
            ]
        };
    }

    public static string Format(MissingSpawnTemplate template)
    {
        var writer = new StringBuilder();
        writer.AppendLine("Missing NPC / Spawn Wizard");
        writer.AppendLine();
        writer.AppendLine($"NPC name: {template.NpcName}");
        writer.AppendLine($"Zone: {template.Zone}");
        writer.AppendLine($"Suggested spawn script: {template.SuggestedSpawnScriptPath}");
        writer.AppendLine();
        writer.AppendLine("Author decisions needed:");
        foreach (var note in template.Notes)
            writer.AppendLine("- " + note);
        writer.AppendLine();
        writer.AppendLine("Lua TODO:");
        writer.AppendLine(template.LuaTodo);
        writer.AppendLine();
        writer.AppendLine("Review-only SQL template:");
        writer.AppendLine(template.CommentedSql);
        return writer.ToString();
    }

    private static string BuildSql(string npcName, string zone, string spawnScriptPath)
    {
        var writer = new StringBuilder();
        writer.AppendLine($"-- Missing NPC: {npcName}");
        writer.AppendLine($"-- Zone: {zone}");
        writer.AppendLine($"-- INSERT INTO spawn (name, targetable, show_name, command_primary, show_level) VALUES ({Utilities.SqlString(npcName)}, 1, 1, 0, 1);");
        writer.AppendLine("-- SET @new_spawn_id = LAST_INSERT_ID();");
        writer.AppendLine("-- INSERT INTO spawn_npcs (spawn_id, min_level, max_level) VALUES (@new_spawn_id, <min_level>, <max_level>);");
        writer.AppendLine("-- INSERT INTO spawn_location_name (name) VALUES ('<location_name>');");
        writer.AppendLine("-- SET @new_location_id = LAST_INSERT_ID();");
        writer.AppendLine("-- INSERT INTO spawn_location_entry (spawn_id, spawn_location_id, spawnpercentage) VALUES (@new_spawn_id, @new_location_id, 100);");
        writer.AppendLine("-- INSERT INTO spawn_location_placement (zone_id, spawn_location_id, x, y, z, heading) VALUES (<zone_id>, @new_location_id, <x>, <y>, <z>, <heading>);");
        writer.AppendLine($"-- INSERT INTO spawn_scripts (spawn_id, lua_script) VALUES (@new_spawn_id, {Utilities.SqlString(spawnScriptPath)});");
        return writer.ToString();
    }

    private static string FirstNonBlank(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }
}
