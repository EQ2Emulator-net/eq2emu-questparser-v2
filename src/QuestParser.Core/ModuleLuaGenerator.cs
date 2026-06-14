using System.Globalization;
using System.Text;

namespace QuestParser.Core;

public sealed class ModuleLuaGenerator
{
    public string Generate(QuestSpec spec)
    {
        var writer = new StringBuilder();
        var questIdentifier = LuaSafeQuestIdentifier(spec.Quest.Name);
        var questId = spec.QuestId.Id ?? 0;
        var author = string.IsNullOrWhiteSpace(spec.Quest.Author) ? "QuestParser" : spec.Quest.Author;
        var fileName = Path.GetFileName(spec.Output.LuaPath);
        var giverName = spec.Giver.Name.Length > 0 ? spec.Giver.Name : spec.Giver.Query;

        writer.AppendLine("--[[");
        writer.AppendLine($"\tScript Name\t\t:\t{HeaderText(fileName)}");
        writer.AppendLine($"\tScript Purpose\t:\tHandles the quest, \"{HeaderText(spec.Quest.Name)}\"");
        writer.AppendLine($"\tScript Author\t:\t{HeaderText(author)}");
        writer.AppendLine($"\tScript Date\t\t:\t{DateTime.Now:d}");
        writer.AppendLine("\tScript Notes\t:\tGenerated with EQ2Emu QuestParser V1 using QuestModule.");
        writer.AppendLine();
        writer.AppendLine($"\tZone\t\t\t:\t{HeaderText(spec.Quest.Zone)}");
        writer.AppendLine($"\tQuest Giver\t\t:\t{HeaderText(giverName)}");
        writer.AppendLine("\tPreceded by\t\t:\tNone");
        writer.AppendLine("\tFollowed by\t\t:\tNone");
        writer.AppendLine("--]]");
        writer.AppendLine();
        writer.AppendLine("require \"SpawnScripts/Generic/QuestModule\"");
        writer.AppendLine();
        writer.AppendLine($"local {questIdentifier} = {questId}");
        writer.AppendLine();

        WriteConstants(writer, spec);
        WriteStageLocals(writer, spec);
        WriteStageHandlers(writer, spec, questIdentifier);
        WriteStageTables(writer, spec);
        WriteAllSteps(writer, spec);
        WriteInit(writer, spec);
        WriteSimpleHandlers(writer);
        WriteQuestComplete(writer, spec);
        WriteReload(writer);

        return writer.ToString();
    }

    private static void WriteConstants(StringBuilder writer, QuestSpec spec)
    {
        var wrote = false;

        if (spec.QuestId.Status == ResolveStatus.Proposed)
        {
            writer.AppendLine("-- TODO DB: Quest ID is proposed. Review generated SQL before loading this quest.");
            wrote = true;
        }

        if (spec.Giver.Id is long giverId)
        {
            writer.AppendLine($"local QUEST_GIVER_ID = {giverId}");
            wrote = true;
        }

        foreach (var step in spec.Stages.SelectMany(stage => stage.Steps))
        {
            if (!step.Target.HasUsableId || step.Target.Id is not long targetId)
                continue;

            writer.AppendLine($"local {TargetConstantName(step)} = {targetId}");
            wrote = true;
        }

        if (wrote)
            writer.AppendLine();
    }

    private static void WriteStageLocals(StringBuilder writer, QuestSpec spec)
    {
        foreach (var stage in spec.Stages)
            writer.AppendLine($"local STAGE_{stage.Number}_STEPS = {{}}");
        writer.AppendLine("local ALL_STEPS = {}");
        writer.AppendLine();

        foreach (var stage in spec.Stages)
        {
            writer.AppendLine($"local CompleteStage{stage.Number}");
            if (stage.IsParallel)
                writer.AppendLine($"local CheckProgressStage{stage.Number}");
        }

        if (spec.Stages.Count > 0)
            writer.AppendLine();
    }

    private static void WriteStageHandlers(StringBuilder writer, QuestSpec spec, string questIdentifier)
    {
        foreach (var stage in spec.Stages)
        {
            writer.AppendLine($"CompleteStage{stage.Number} = function(Quest, QuestGiver, Player)");
            writer.AppendLine($"\tUpdateQuestTaskGroupDescription(Quest, {stage.Number}, {Utilities.LuaString(stage.CompletedDescription)})");

            var nextStage = spec.Stages.FirstOrDefault(candidate => candidate.Number == stage.Number + 1);
            if (nextStage is null)
                writer.AppendLine("\tQuestComplete(Quest, QuestGiver, Player)");
            else
                writer.AppendLine($"\tQuestModule.AddSteps(Quest, STAGE_{nextStage.Number}_STEPS)");

            writer.AppendLine("end");
            writer.AppendLine();

            if (stage.IsParallel)
                WriteCheckProgress(writer, stage, questIdentifier);
        }
    }

    private static void WriteCheckProgress(StringBuilder writer, QuestStageSpec stage, string questIdentifier)
    {
        writer.AppendLine($"CheckProgressStage{stage.Number} = function(Quest, QuestGiver, Player)");
        var conditions = stage.Steps.Select(step => $"QuestStepIsComplete(Player, {questIdentifier}, {step.Number})").ToArray();
        writer.AppendLine("\tif " + (conditions.Length == 0 ? "true" : string.Join(" and ", conditions)) + " then");
        writer.AppendLine($"\t\tCompleteStage{stage.Number}(Quest, QuestGiver, Player)");
        writer.AppendLine("\tend");
        writer.AppendLine("end");
        writer.AppendLine();
    }

    private static void WriteStageTables(StringBuilder writer, QuestSpec spec)
    {
        foreach (var stage in spec.Stages)
        {
            writer.AppendLine($"STAGE_{stage.Number}_STEPS = {{");
            foreach (var step in stage.Steps)
                WriteStepRecord(writer, stage, step);
            writer.AppendLine("}");
            writer.AppendLine();
        }
    }

    private static void WriteStepRecord(StringBuilder writer, QuestStageSpec stage, QuestStepSpec step)
    {
        foreach (var todo in TodosForStep(step))
            writer.AppendLine($"\t-- TODO DB: {todo}");

        writer.AppendLine("\t{");
        writer.AppendLine($"\t\tid = {step.Number},");
        writer.AppendLine($"\t\ttype = {Utilities.LuaString(ModuleStepType(step.Type))},");
        writer.AppendLine($"\t\ttext = {Utilities.LuaString(step.Description)},");
        writer.AppendLine($"\t\tcount = {QuantityExpression(step.QuantityMin, step.QuantityMax)},");
        writer.AppendLine($"\t\tpercentage = {Number(step.Percentage)},");
        writer.AppendLine($"\t\ttaskGroupText = {Utilities.LuaString(stage.Description)},");
        writer.AppendLine($"\t\ttaskGroupDescription = {Utilities.LuaString(stage.Description)},");
        writer.AppendLine($"\t\ticon = {step.IconId},");
        if (step.HasRandomOptions)
            WriteRandomOptions(writer, step);
        else if (IsLocationStep(step.Type))
            WriteLocationFields(writer, step);
        else if (HasTargetIds(step.Target))
            writer.AppendLine($"\t\ttargets = {TargetList(step)},");
        writer.AppendLine($"\t\tcomplete = {Utilities.LuaString($"Step{step.Number}Complete")},");
        writer.AppendLine($"\t\tcompleteText = {Utilities.LuaString(step.CompletedDescription)},");
        writer.AppendLine($"\t\tcompleteDescription = {Utilities.LuaString(step.CompletedDescription)},");
        writer.AppendLine($"\t\tcompleteTaskGroup = {Utilities.LuaString(stage.CompletedDescription)},");
        if (!stage.IsParallel)
        {
            writer.AppendLine($"\t\tcompleteTaskGroupId = {stage.Number},");
            writer.AppendLine($"\t\tcompleteTaskGroupDescription = {Utilities.LuaString(stage.CompletedDescription)},");
        }
        writer.AppendLine($"\t\tonComplete = {OnCompleteHandler(stage)}");
        writer.AppendLine("\t},");
    }

    private static void WriteRandomOptions(StringBuilder writer, QuestStepSpec step)
    {
        writer.AppendLine("\t\trandomOptions = {");
        foreach (var option in step.RandomOptions)
        {
            writer.AppendLine("\t\t\t{");
            writer.AppendLine($"\t\t\t\ttext = {Utilities.LuaString(option.Description)},");
            writer.AppendLine($"\t\t\t\tcount = {QuantityExpression(option.QuantityMin, option.QuantityMax)},");
            writer.AppendLine($"\t\t\t\tpercentage = {Number(option.Percentage)},");
            writer.AppendLine($"\t\t\t\ticon = {option.IconId},");
            if (HasTargetIds(option.Target))
                writer.AppendLine($"\t\t\t\ttargets = {TargetList(option.Target)}");
            writer.AppendLine("\t\t\t},");
        }
        writer.AppendLine("\t\t},");
    }

    private static void WriteLocationFields(StringBuilder writer, QuestStepSpec step)
    {
        var location = step.Location ?? new LocationTarget();
        var radius = location.Radius <= 0 ? 10 : location.Radius;
        writer.AppendLine($"\t\tmaxVariation = {Number(radius)},");
        writer.AppendLine("\t\tlocations = {");
        if (step.Type == StepType.ZoneLocation)
        {
            var zoneId = location.Zone.Id ?? 0;
            writer.AppendLine($"\t\t\t{{ x = {Number(location.X)}, y = {Number(location.Y)}, z = {Number(location.Z)}, zone = {zoneId} }}");
        }
        else
        {
            writer.AppendLine($"\t\t\t{{ x = {Number(location.X)}, y = {Number(location.Y)}, z = {Number(location.Z)} }}");
        }
        writer.AppendLine("\t\t},");
    }

    private static void WriteAllSteps(StringBuilder writer, QuestSpec spec)
    {
        foreach (var stage in spec.Stages)
        {
            writer.AppendLine($"QuestModule.ExportStepHandlers(STAGE_{stage.Number}_STEPS, {{ overwrite = true }})");
            writer.AppendLine($"for _, step in ipairs(STAGE_{stage.Number}_STEPS) do");
            writer.AppendLine("\tALL_STEPS[#ALL_STEPS + 1] = step");
            writer.AppendLine("end");
            writer.AppendLine();
        }
    }

    private static void WriteInit(StringBuilder writer, QuestSpec spec)
    {
        writer.AppendLine("function Init(Quest)");
        if (spec.Quest.IsTradeskill)
            writer.AppendLine("\tSetQuestFeatherColor(Quest, 2)");
        if (spec.Quest.Repeatable)
        {
            if (!spec.Quest.IsTradeskill)
                writer.AppendLine("\tSetQuestFeatherColor(Quest, 3)");
            writer.AppendLine("\tSetQuestRepeatable(Quest)");
        }

        if (HasStaticRewards(spec))
            writer.AppendLine("\t-- Static quest rewards are generated in quest_details SQL to avoid duplicate Lua + DB rewards.");

        var firstStage = spec.Stages.OrderBy(stage => stage.Number).FirstOrDefault();
        if (firstStage is not null)
            writer.AppendLine($"\tQuestModule.AddSteps(Quest, STAGE_{firstStage.Number}_STEPS)");

        writer.AppendLine("end");
        writer.AppendLine();
    }

    private static bool HasStaticRewards(QuestSpec spec)
    {
        return spec.Rewards.CoinMin > 0
            || spec.Rewards.CoinMax > 0
            || spec.Rewards.Experience > 0
            || spec.Rewards.Items.Count > 0
            || spec.Rewards.Factions.Count > 0;
    }

    private static void WriteSimpleHandlers(StringBuilder writer)
    {
        writer.AppendLine("function Accepted(Quest, QuestGiver, Player)");
        writer.AppendLine("end");
        writer.AppendLine();
        writer.AppendLine("function Declined(Quest, QuestGiver, Player)");
        writer.AppendLine("end");
        writer.AppendLine();
        writer.AppendLine("function Deleted(Quest, QuestGiver, Player)");
        writer.AppendLine("end");
        writer.AppendLine();
    }

    private static void WriteQuestComplete(StringBuilder writer, QuestSpec spec)
    {
        writer.AppendLine("function QuestComplete(Quest, QuestGiver, Player)");
        writer.AppendLine($"\tUpdateQuestDescription(Quest, {Utilities.LuaString(spec.Quest.CompletionText)})");
        writer.AppendLine("\tGiveQuestReward(Quest, Player)");
        writer.AppendLine("end");
        writer.AppendLine();
    }

    private static void WriteReload(StringBuilder writer)
    {
        writer.AppendLine("function Reload(Quest, QuestGiver, Player, Step)");
        writer.AppendLine("\tQuestModule.ReloadByStep(Quest, QuestGiver, Player, Step, nil, ALL_STEPS)");
        writer.AppendLine("end");
    }

    private static string TargetConstantName(QuestStepSpec step)
    {
        var kind = QuestSpecFactory.KindForStepType(step.Type).ToUpperInvariant();
        return $"STEP_{step.Number}_{kind}_ID";
    }

    private static string TargetList(QuestStepSpec step)
    {
        if (step.Target.HasUsableId && step.Target.Id is long)
            return $"{{ {TargetConstantName(step)} }}";

        return TargetList(step.Target);
    }

    private static string TargetList(ResolvedReference reference)
    {
        if (reference.HasUsableId && reference.Id is long id)
            return $"{{ {id} }}";

        if (reference.Ids.Count > 0)
            return "{ " + string.Join(", ", reference.Ids) + " }";

        return "{}";
    }

    private static bool HasTargetIds(ResolvedReference reference)
    {
        return reference.HasUsableId || reference.Ids.Count > 0;
    }

    private static string OnCompleteHandler(QuestStageSpec stage)
    {
        return stage.IsParallel
            ? $"CheckProgressStage{stage.Number}"
            : $"CompleteStage{stage.Number}";
    }

    private static string ModuleStepType(StepType type)
    {
        return type switch
        {
            StepType.Generic => "basic",
            StepType.Chat => "chat",
            StepType.Craft => "craft",
            StepType.Harvest => "harvest",
            StepType.Kill => "kill",
            StepType.KillByRace => "killByRace",
            StepType.Location => "location",
            StepType.ObtainItem => "obtainItem",
            StepType.Spell => "spell",
            StepType.ZoneLocation => "zoneLoc",
            _ => "basic"
        };
    }

    private static bool IsLocationStep(StepType type)
    {
        return type is StepType.Location or StepType.ZoneLocation;
    }

    private static IEnumerable<string> TodosForStep(QuestStepSpec step)
    {
        if (step.HasRandomOptions)
        {
            return step.RandomOptions
                .Select((option, index) => TodoForReference(option.Target, $"random option {index + 1} on step {step.Number}"))
                .Where(todo => todo.Length > 0);
        }

        if (step.Type is StepType.Location or StepType.ZoneLocation)
            return step.Location is null ? [$"Set coordinates for step {step.Number}."] : [];

        var todo = TodoForReference(step.Target, $"step {step.Number}");
        return todo.Length == 0 ? [] : [todo];
    }

    private static string TodoForReference(ResolvedReference reference, string context)
    {
        return reference.Status switch
        {
            ResolveStatus.Missing => $"Resolve {reference.Kind} '{reference.Query}' for {context}.",
            ResolveStatus.Ambiguous => $"Choose {reference.Kind} for '{reference.Query}' from candidates: {string.Join(", ", reference.Candidates.Select(c => c.Id))}.",
            _ => ""
        };
    }

    private static string QuantityExpression(int quantityMin, int quantityMax)
    {
        var max = quantityMax <= 0 ? 1 : quantityMax;
        if (quantityMin > 0 && max > quantityMin)
            return $"{{ min = {quantityMin}, max = {max} }}";
        return max.ToString(CultureInfo.InvariantCulture);
    }

    private static string Number(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string HeaderText(string value)
    {
        return value
            .Replace("]]", "] ]", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }

    private static string LuaSafeQuestIdentifier(string questName)
    {
        var identifier = Utilities.IdentifierFromName(questName);
        return char.IsLetter(identifier[0]) || identifier[0] == '_'
            ? identifier
            : "Quest" + identifier;
    }
}
