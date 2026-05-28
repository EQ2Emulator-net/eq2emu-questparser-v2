using System.Text;

namespace QuestParser.Core;

public sealed class LuaGenerator
{
    public string Generate(QuestSpec spec)
    {
        var writer = new StringBuilder();
        var questIdentifier = Utilities.IdentifierFromName(spec.Quest.Name);
        var questId = spec.QuestId.Id ?? 0;
        var author = string.IsNullOrWhiteSpace(spec.Quest.Author) ? "QuestParser" : spec.Quest.Author;
        var fileName = Path.GetFileName(spec.Output.LuaPath);

        writer.AppendLine("--[[");
        writer.AppendLine($"\tScript Name\t\t:\t{fileName}");
        writer.AppendLine($"\tScript Purpose\t:\tHandles the quest, \"{spec.Quest.Name}\"");
        writer.AppendLine($"\tScript Author\t:\t{author}");
        writer.AppendLine($"\tScript Date\t\t:\t{DateTime.Now:d}");
        writer.AppendLine("\tScript Notes\t:\tGenerated with EQ2Emu QuestParser V1.");
        writer.AppendLine();
        writer.AppendLine($"\tZone\t\t\t:\t{spec.Quest.Zone}");
        writer.AppendLine($"\tQuest Giver\t\t:\t{(spec.Giver.Name.Length > 0 ? spec.Giver.Name : spec.Giver.Query)}");
        writer.AppendLine("\tPreceded by\t\t:\tNone");
        writer.AppendLine("\tFollowed by\t\t:\tNone");
        writer.AppendLine("--]]");
        writer.AppendLine();
        writer.AppendLine($"local {questIdentifier} = {questId}");
        writer.AppendLine();

        WriteConstants(writer, spec);
        WriteInit(writer, spec);
        WriteSimpleHandlers(writer);

        foreach (var stage in spec.Stages)
            WriteStageAdder(writer, spec, stage);

        foreach (var stage in spec.Stages)
        {
            foreach (var step in stage.Steps)
                WriteStepComplete(writer, spec, stage, step);

            if (stage.IsParallel)
                WriteCheckProgress(writer, spec, stage);
        }

        WriteQuestComplete(writer, spec);
        WriteReload(writer, spec);

        return writer.ToString();
    }

    private static void WriteConstants(StringBuilder writer, QuestSpec spec)
    {
        if (spec.QuestId.Status == ResolveStatus.Proposed)
            writer.AppendLine("-- TODO DB: Quest ID is proposed. Review generated SQL before loading this quest.");

        if (spec.Giver.Id is long giverId)
            writer.AppendLine($"local QUEST_GIVER_ID = {giverId}");

        foreach (var step in spec.Stages.SelectMany(stage => stage.Steps))
        {
            if (step.Target.Id is not long targetId)
                continue;

            var kind = QuestSpecFactory.KindForStepType(step.Type).ToUpperInvariant();
            writer.AppendLine($"local STEP_{step.Number}_{kind}_ID = {targetId}");
        }

        if (writer.Length > 0)
            writer.AppendLine();
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
        writer.AppendLine("\tAddStage1Steps(Quest)");
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

    private static void WriteStageAdder(StringBuilder writer, QuestSpec spec, QuestStageSpec stage)
    {
        writer.AppendLine($"function AddStage{stage.Number}Steps(Quest)");
        foreach (var step in stage.Steps)
        {
            foreach (var todo in TodosForStep(step))
                writer.AppendLine($"\t-- TODO DB: {todo}");

            if (step.HasRandomOptions)
                WriteRandomStep(writer, step, stage.Description);
            else
                writer.AppendLine("\t" + BuildAddStepCall(step, stage.Description));

            writer.AppendLine($"\tAddQuestStepCompleteAction(Quest, {step.Number}, \"Step{step.Number}Complete\")");
        }

        writer.AppendLine("end");
        writer.AppendLine();
    }

    private static void WriteRandomStep(StringBuilder writer, QuestStepSpec step, string taskGroup)
    {
        writer.AppendLine($"\tlocal choice = MakeRandomInt(1, {step.RandomOptions.Count})");
        for (var i = 0; i < step.RandomOptions.Count; i++)
        {
            var keyword = i == 0 ? "if" : "elseif";
            writer.AppendLine($"\t{keyword} choice == {i + 1} then");
            writer.AppendLine("\t\t" + BuildAddStepCall(step, step.RandomOptions[i], taskGroup));
        }
        writer.AppendLine("\tend");
    }

    private static void WriteStepComplete(StringBuilder writer, QuestSpec spec, QuestStageSpec stage, QuestStepSpec step)
    {
        writer.AppendLine($"function Step{step.Number}Complete(Quest, QuestGiver, Player)");
        writer.AppendLine($"\tUpdateQuestStepDescription(Quest, {step.Number}, {Utilities.LuaString(step.CompletedDescription)})");
        if (stage.IsParallel)
            writer.AppendLine($"\tCheckProgressStage{stage.Number}(Quest, QuestGiver, Player)");
        else
            WriteAdvance(writer, spec, stage);
        writer.AppendLine("end");
        writer.AppendLine();
    }

    private static void WriteCheckProgress(StringBuilder writer, QuestSpec spec, QuestStageSpec stage)
    {
        writer.AppendLine($"function CheckProgressStage{stage.Number}(Quest, QuestGiver, Player)");
        var questIdName = Utilities.IdentifierFromName(spec.Quest.Name);
        var conditions = stage.Steps.Select(step => $"QuestStepIsComplete(Player, {questIdName}, {step.Number})");
        writer.AppendLine("\tif " + string.Join(" and ", conditions) + " then");
        writer.AppendLine($"\t\tUpdateQuestTaskGroupDescription(Quest, {stage.Number}, {Utilities.LuaString(stage.CompletedDescription)})");
        WriteAdvance(writer, spec, stage, "\t\t");
        writer.AppendLine("\tend");
        writer.AppendLine("end");
        writer.AppendLine();
    }

    private static void WriteAdvance(StringBuilder writer, QuestSpec spec, QuestStageSpec currentStage, string indent = "\t")
    {
        if (!currentStage.IsParallel)
            writer.AppendLine($"{indent}UpdateQuestTaskGroupDescription(Quest, {currentStage.Number}, {Utilities.LuaString(currentStage.CompletedDescription)})");

        var nextStage = spec.Stages.FirstOrDefault(stage => stage.Number == currentStage.Number + 1);
        if (nextStage is null)
            writer.AppendLine($"{indent}QuestComplete(Quest, QuestGiver, Player)");
        else
            writer.AppendLine($"{indent}AddStage{nextStage.Number}Steps(Quest)");
    }

    private static void WriteQuestComplete(StringBuilder writer, QuestSpec spec)
    {
        writer.AppendLine("function QuestComplete(Quest, QuestGiver, Player)");
        writer.AppendLine($"\tUpdateQuestDescription(Quest, {Utilities.LuaString(spec.Quest.CompletionText)})");
        writer.AppendLine("\tGiveQuestReward(Quest, Player)");
        writer.AppendLine("end");
        writer.AppendLine();
    }

    private static void WriteReload(StringBuilder writer, QuestSpec spec)
    {
        var allSteps = spec.Stages.SelectMany(stage => stage.Steps).OrderBy(step => step.Number).ToArray();
        writer.AppendLine("function Reload(Quest, QuestGiver, Player, Step)");
        for (var i = 0; i < allSteps.Length; i++)
        {
            var keyword = i == 0 ? "if" : "elseif";
            writer.AppendLine($"\t{keyword} Step == {allSteps[i].Number} then");
            writer.AppendLine($"\t\tStep{allSteps[i].Number}Complete(Quest, QuestGiver, Player)");
        }
        writer.AppendLine("\tend");
        writer.AppendLine("end");
    }

    private static string BuildAddStepCall(QuestStepSpec step, string taskGroup)
    {
        var description = Utilities.LuaString(step.Description);
        var group = Utilities.LuaString(taskGroup);
        var targetIds = TargetIds(step);
        var quantity = QuantityExpression(step.QuantityMin, step.QuantityMax);
        var percent = step.Percentage.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        return step.Type switch
        {
            StepType.Chat => $"AddQuestStepChat(Quest, {step.Number}, {description}, {quantity}, {group}, {step.IconId}{targetIds})",
            StepType.Kill => $"AddQuestStepKill(Quest, {step.Number}, {description}, {quantity}, {percent}, {group}, {step.IconId}{targetIds})",
            StepType.KillByRace => $"AddQuestStepKillByRace(Quest, {step.Number}, {description}, {quantity}, {percent}, {group}, {step.IconId}{targetIds})",
            StepType.Harvest => $"AddQuestStepHarvest(Quest, {step.Number}, {description}, {quantity}, {percent}, {group}, {step.IconId}{targetIds})",
            StepType.ObtainItem => $"AddQuestStepObtainItem(Quest, {step.Number}, {description}, {quantity}, {percent}, {group}, {step.IconId}{targetIds})",
            StepType.Craft => $"AddQuestStepCraft(Quest, {step.Number}, {description}, {quantity}, {percent}, {group}, {step.IconId}{targetIds})",
            StepType.Spell => $"AddQuestStepSpell(Quest, {step.Number}, {description}, {quantity}, {percent}, {group}, {step.IconId}{targetIds})",
            StepType.Location => BuildLocationCall(step, group, zoneLocation: false),
            StepType.ZoneLocation => BuildLocationCall(step, group, zoneLocation: true),
            _ => $"AddQuestStep(Quest, {step.Number}, {description}, {quantity}, {percent}, {group}, {step.IconId}, 0{targetIds})"
        };
    }

    private static string BuildAddStepCall(QuestStepSpec step, QuestStepOptionSpec option, string taskGroup)
    {
        var description = Utilities.LuaString(option.Description);
        var group = Utilities.LuaString(taskGroup);
        var targetIds = TargetIds(option);
        var quantity = QuantityExpression(option.QuantityMin, option.QuantityMax);
        var percent = option.Percentage.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        return step.Type switch
        {
            StepType.Chat => $"AddQuestStepChat(Quest, {step.Number}, {description}, {quantity}, {group}, {option.IconId}{targetIds})",
            StepType.Kill => $"AddQuestStepKill(Quest, {step.Number}, {description}, {quantity}, {percent}, {group}, {option.IconId}{targetIds})",
            StepType.KillByRace => $"AddQuestStepKillByRace(Quest, {step.Number}, {description}, {quantity}, {percent}, {group}, {option.IconId}{targetIds})",
            StepType.Harvest => $"AddQuestStepHarvest(Quest, {step.Number}, {description}, {quantity}, {percent}, {group}, {option.IconId}{targetIds})",
            StepType.ObtainItem => $"AddQuestStepObtainItem(Quest, {step.Number}, {description}, {quantity}, {percent}, {group}, {option.IconId}{targetIds})",
            StepType.Craft => $"AddQuestStepCraft(Quest, {step.Number}, {description}, {quantity}, {percent}, {group}, {option.IconId}{targetIds})",
            StepType.Spell => $"AddQuestStepSpell(Quest, {step.Number}, {description}, {quantity}, {percent}, {group}, {option.IconId}{targetIds})",
            _ => $"AddQuestStep(Quest, {step.Number}, {description}, {quantity}, {percent}, {group}, {option.IconId}, 0{targetIds})"
        };
    }

    private static string BuildLocationCall(QuestStepSpec step, string taskGroup, bool zoneLocation)
    {
        var description = Utilities.LuaString(step.Description);
        var location = step.Location ?? new LocationTarget();
        var x = Number(location.X);
        var y = Number(location.Y);
        var z = Number(location.Z);
        var radius = Number(location.Radius <= 0 ? 10 : location.Radius);
        var zone = location.Zone.Id ?? 0;
        return zoneLocation
            ? $"AddQuestStepZoneLoc(Quest, {step.Number}, {description}, {radius}, {taskGroup}, {step.IconId}, {x}, {y}, {z}, {zone})"
            : $"AddQuestStepLocation(Quest, {step.Number}, {description}, {radius}, {taskGroup}, {step.IconId}, {x}, {y}, {z})";
    }

    private static string TargetIds(QuestStepSpec step)
    {
        if (step.Target.Id is long id)
            return $", {id}";
        if (step.Target.Ids.Count > 0)
            return ", " + string.Join(", ", step.Target.Ids);
        return "";
    }

    private static string TargetIds(QuestStepOptionSpec option)
    {
        if (option.Target.Id is long id)
            return $", {id}";
        if (option.Target.Ids.Count > 0)
            return ", " + string.Join(", ", option.Target.Ids);
        return "";
    }

    private static IEnumerable<string> TodosForStep(QuestStepSpec step)
    {
        if (step.Type is StepType.Location or StepType.ZoneLocation)
            return [$"Set coordinates for step {step.Number}."];

        if (step.HasRandomOptions)
        {
            return step.RandomOptions
                .Select((option, index) => TodoForReference(option.Target, $"random option {index + 1} on step {step.Number}"))
                .Where(todo => todo.Length > 0);
        }

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
            return $"MakeRandomInt({quantityMin}, {max})";
        return max.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Number(float value)
    {
        return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }
}
