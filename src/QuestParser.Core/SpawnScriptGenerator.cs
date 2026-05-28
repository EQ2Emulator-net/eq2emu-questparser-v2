using System.Text;

namespace QuestParser.Core;

public sealed class SpawnScriptGenerator
{
    public string Generate(QuestSpec spec)
    {
        var writer = new StringBuilder();
        var questIdentifier = Utilities.IdentifierFromName(spec.Quest.Name);
        var questId = spec.QuestId.Id ?? 0;
        var giverName = QuestGiverName(spec);
        var offerFunction = "Offer" + questIdentifier;
        var canOfferFunction = "CanOffer" + questIdentifier;
        var author = string.IsNullOrWhiteSpace(spec.Quest.Author) ? "QuestParser" : spec.Quest.Author;

        writer.AppendLine("--[[");
        writer.AppendLine($"\tScript Name\t\t:\t{BuildExampleRelativePath(spec)}");
        writer.AppendLine($"\tScript Purpose\t:\tExample starter script for \"{spec.Quest.Name}\"");
        writer.AppendLine($"\tScript Author\t:\t{author}");
        writer.AppendLine($"\tScript Date\t\t:\t{DateTime.Now:d}");
        writer.AppendLine("\tScript Notes\t:\tGenerated with EQ2Emu QuestParser V1 as a review aid.");
        writer.AppendLine("\t\t\t\t:\tMerge the relevant hook into the real spawn or item script.");
        writer.AppendLine();
        writer.AppendLine($"\tZone\t\t\t:\t{spec.Quest.Zone}");
        writer.AppendLine($"\tQuest Giver\t\t:\t{giverName}");
        writer.AppendLine($"\tSuggested live spawn script\t:\t{BuildLiveScriptRelativePath(spec)}");
        writer.AppendLine($"\tInventory item script hint\t:\t{BuildItemScriptRelativePath(spec)}");
        writer.AppendLine("--]]");
        writer.AppendLine();
        if (questId == 0)
            writer.AppendLine("-- TODO DB: Resolve the quest ID before loading this script.");
        writer.AppendLine($"local {questIdentifier} = {questId}");
        if (spec.Giver.Id is long giverId)
            writer.AppendLine($"local QUEST_GIVER_ID = {giverId}");
        writer.AppendLine();

        writer.AppendLine("function spawn(NPC)");
        writer.AppendLine($"\tProvidesQuest(NPC, {questIdentifier})");
        writer.AppendLine("\t-- For clickable widgets, enable the command or hand icon only when appropriate.");
        writer.AppendLine("\t-- SpawnSet(NPC, \"show_command_icon\", 1)");
        writer.AppendLine("\t-- SpawnSet(NPC, \"display_hand_icon\", 1)");
        writer.AppendLine("\t-- SetAccessToEntityCommand(Spawn, NPC, \"inspect\", 1) can be used from proximity/access callbacks.");
        writer.AppendLine("end");
        writer.AppendLine();
        writer.AppendLine("function respawn(NPC)");
        writer.AppendLine("\tspawn(NPC)");
        writer.AppendLine("end");
        writer.AppendLine();

        writer.AppendLine("function hailed(NPC, Spawn)");
        writer.AppendLine("\tFaceTarget(NPC, Spawn)");
        writer.AppendLine($"\tif {canOfferFunction}(Spawn) then");
        writer.AppendLine("\t\tlocal conversation = CreateConversation()");
        writer.AppendLine($"\t\tAddConversationOption(conversation, \"I can help.\", \"{offerFunction}\")");
        writer.AppendLine("\t\tAddConversationOption(conversation, \"Not right now.\")");
        writer.AppendLine("\t\tStartConversation(conversation, NPC, Spawn, \"TODO: Add quest starter dialog.\")");
        writer.AppendLine($"\telseif HasQuest(Spawn, {questIdentifier}) then");
        writer.AppendLine("\t\tPlayFlavor(NPC, \"\", \"TODO: Add in-progress or return dialog.\", \"\", 0, 0, Spawn)");
        writer.AppendLine("\telse");
        writer.AppendLine("\t\tPlayFlavor(NPC, \"\", \"TODO: Add post-quest dialog.\", \"\", 0, 0, Spawn)");
        writer.AppendLine("\tend");
        writer.AppendLine("end");
        writer.AppendLine();

        writer.AppendLine("function casted_on(Target, Caster, SpellName)");
        writer.AppendLine("\t-- Common for clickable or inspectable world objects. Confirm the live SpellName.");
        writer.AppendLine($"\tif {canOfferFunction}(Caster) and (SpellName == nil or SpellName == \"inspect\" or SpellName == \"examine\") then");
        writer.AppendLine($"\t\t{offerFunction}(Target, Caster)");
        writer.AppendLine("\tend");
        writer.AppendLine("end");
        writer.AppendLine();

        writer.AppendLine("function used(NPC, Spawn, SpellName)");
        writer.AppendLine("\t-- Some world objects call used instead of casted_on.");
        writer.AppendLine($"\tif {canOfferFunction}(Spawn) then");
        writer.AppendLine($"\t\t{offerFunction}(NPC, Spawn)");
        writer.AppendLine("\tend");
        writer.AppendLine("end");
        writer.AppendLine();

        writer.AppendLine("function examined(NPC, Spawn)");
        writer.AppendLine("\t-- Rare for spawn scripts, but useful for examine-style world objects.");
        writer.AppendLine($"\tif {canOfferFunction}(Spawn) then");
        writer.AppendLine($"\t\t{offerFunction}(NPC, Spawn)");
        writer.AppendLine("\tend");
        writer.AppendLine("end");
        writer.AppendLine();

        writer.AppendLine($"function {canOfferFunction}(Player)");
        writer.AppendLine($"\treturn not HasQuest(Player, {questIdentifier}) and not HasCompletedQuest(Player, {questIdentifier})");
        writer.AppendLine("end");
        writer.AppendLine();

        writer.AppendLine($"function {offerFunction}(NPC, Spawn)");
        writer.AppendLine($"\tif {canOfferFunction}(Spawn) then");
        writer.AppendLine($"\t\tOfferQuest(NPC, Spawn, {questIdentifier})");
        writer.AppendLine("\tend");
        writer.AppendLine("end");
        writer.AppendLine();

        writer.AppendLine("-- Inventory item variant: move this hook to ItemScripts/<item>.lua when the quest starts from an item.");
        writer.AppendLine("-- function examined(Item, Player)");
        writer.AppendLine($"-- \tif not HasQuest(Player, {questIdentifier}) and not HasCompletedQuest(Player, {questIdentifier}) then");
        writer.AppendLine($"-- \t\tOfferQuest(nil, Player, {questIdentifier})");
        writer.AppendLine("-- \tend");
        writer.AppendLine("-- end");

        return writer.ToString();
    }

    public static string QuestGiverName(QuestSpec spec)
    {
        if (!string.IsNullOrWhiteSpace(spec.Giver.Name))
            return spec.Giver.Name;
        if (!string.IsNullOrWhiteSpace(spec.Giver.Query))
            return spec.Giver.Query;
        return spec.QuestGivers.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "Quest Giver";
    }

    public static string BuildExamplePath(QuestSpec spec)
    {
        return BuildExamplePath(spec.Output.ContentRoot, spec.Quest.Zone, QuestGiverName(spec));
    }

    public static string BuildExamplePath(string contentRoot, string zone, string giverName)
    {
        return Path.Combine(contentRoot, "SpawnScripts", Utilities.SafeDirectoryName(zone), Utilities.NormalizeSpawnScriptExampleFileName(giverName));
    }

    public static string BuildExampleRelativePath(QuestSpec spec)
    {
        return Path.Combine("SpawnScripts", Utilities.SafeDirectoryName(spec.Quest.Zone), Utilities.NormalizeSpawnScriptExampleFileName(QuestGiverName(spec))).Replace('\\', '/');
    }

    public static string BuildLiveScriptRelativePath(QuestSpec spec)
    {
        return Path.Combine("SpawnScripts", Utilities.SafeDirectoryName(spec.Quest.Zone), Utilities.NormalizeSpawnScriptFileName(QuestGiverName(spec))).Replace('\\', '/');
    }

    private static string BuildItemScriptRelativePath(QuestSpec spec)
    {
        return Path.Combine("ItemScripts", Utilities.NormalizeSpawnScriptFileName(QuestGiverName(spec))).Replace('\\', '/');
    }
}
