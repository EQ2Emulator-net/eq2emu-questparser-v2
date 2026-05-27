namespace QuestParser.Core;

public static class QuestSpecValidator
{
    public static List<QuestDiagnostic> Validate(QuestSpec spec, bool overwrite = false)
    {
        var diagnostics = new List<QuestDiagnostic>();

        if (string.IsNullOrWhiteSpace(spec.Quest.Name))
            Add(diagnostics, QuestDiagnosticSeverity.Blocker, "quest", "QUEST_NAME", "Quest name is required.");
        if (string.IsNullOrWhiteSpace(spec.Quest.Zone))
            Add(diagnostics, QuestDiagnosticSeverity.Warning, "quest", "QUEST_ZONE", "Quest zone/category is blank.");

        AddReferenceDiagnostic(diagnostics, "quest", "QUEST_ID", "Quest DB id", spec.QuestId, allowProposed: true);
        AddReferenceDiagnostic(diagnostics, "giver", "QUEST_GIVER", "Quest giver", spec.Giver, allowProposed: false);

        if (spec.QuestId.Status == ResolveStatus.Proposed)
            Add(diagnostics, QuestDiagnosticSeverity.Warning, "quest", "QUEST_ID_PROPOSED", $"Quest id {spec.QuestId.Id} is proposed. Review SQL before applying DB changes.");

        foreach (var stage in spec.Stages)
        {
            if (string.IsNullOrWhiteSpace(stage.Description))
                Add(diagnostics, QuestDiagnosticSeverity.Warning, $"stage:{stage.Number}", "STAGE_TEXT", $"Stage {stage.Number} has no task group text.");

            foreach (var step in stage.Steps)
                ValidateStep(diagnostics, stage, step);
        }

        for (var i = 0; i < spec.Rewards.Items.Count; i++)
        {
            var reward = spec.Rewards.Items[i];
            if (!string.IsNullOrWhiteSpace(reward.Item.Query) || !string.IsNullOrWhiteSpace(reward.Item.Name))
                AddReferenceDiagnostic(diagnostics, "rewards", $"REWARD_ITEM_{i + 1}", $"Reward item {i + 1}", reward.Item, allowProposed: false);
        }

        for (var i = 0; i < spec.Rewards.Factions.Count; i++)
        {
            var reward = spec.Rewards.Factions[i];
            if (!string.IsNullOrWhiteSpace(reward.Faction.Query) || !string.IsNullOrWhiteSpace(reward.Faction.Name))
                AddReferenceDiagnostic(diagnostics, "rewards", $"REWARD_FACTION_{i + 1}", $"Reward faction {i + 1}", reward.Faction, allowProposed: false);
        }

        if (string.IsNullOrWhiteSpace(spec.Output.LuaPath))
            Add(diagnostics, QuestDiagnosticSeverity.Blocker, "output", "LUA_PATH", "Lua output path is required.");
        else if (File.Exists(spec.Output.LuaPath) && !overwrite)
            Add(diagnostics, QuestDiagnosticSeverity.Blocker, "output", "LUA_EXISTS", $"Lua file already exists and overwrite is off: {spec.Output.LuaPath}");

        if (string.IsNullOrWhiteSpace(spec.Output.SpecPath))
            Add(diagnostics, QuestDiagnosticSeverity.Blocker, "output", "SPEC_PATH", "Spec output path is required.");
        if (string.IsNullOrWhiteSpace(spec.Output.SqlPath))
            Add(diagnostics, QuestDiagnosticSeverity.Warning, "output", "SQL_PATH", "SQL output path is blank.");
        if (string.IsNullOrWhiteSpace(spec.Output.MissingReportPath))
            Add(diagnostics, QuestDiagnosticSeverity.Warning, "output", "MISSING_PATH", "Missing report path is blank.");

        return diagnostics
            .OrderByDescending(d => d.Severity)
            .ThenBy(d => d.SectionKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ValidateStep(List<QuestDiagnostic> diagnostics, QuestStageSpec stage, QuestStepSpec step)
    {
        var sectionKey = $"step:{step.Number}";

        if (string.IsNullOrWhiteSpace(step.Description))
            Add(diagnostics, QuestDiagnosticSeverity.Blocker, sectionKey, "STEP_TEXT", $"Step {step.Number} has no description.");
        if (step.QuantityMax <= 0)
            Add(diagnostics, QuestDiagnosticSeverity.Blocker, sectionKey, "STEP_QUANTITY", $"Step {step.Number} quantity must be greater than zero.");
        if (step.Type == StepType.Generic)
            Add(diagnostics, QuestDiagnosticSeverity.Warning, sectionKey, "STEP_GENERIC", $"Step {step.Number} uses generic AddQuestStep. Verify this is intentional.");

        if (step.Type is StepType.Location or StepType.ZoneLocation)
        {
            if (step.Location is null)
            {
                Add(diagnostics, QuestDiagnosticSeverity.Blocker, sectionKey, "LOCATION_MISSING", $"Step {step.Number} is a location step without location data.");
                return;
            }

            if (step.Location.X == 0 && step.Location.Y == 0 && step.Location.Z == 0)
                Add(diagnostics, QuestDiagnosticSeverity.Blocker, sectionKey, "LOCATION_COORDS", $"Step {step.Number} needs reviewed coordinates.");
            if (step.Location.Radius <= 0)
                Add(diagnostics, QuestDiagnosticSeverity.Blocker, sectionKey, "LOCATION_RADIUS", $"Step {step.Number} location radius must be greater than zero.");
            if (step.Type == StepType.ZoneLocation)
                AddReferenceDiagnostic(diagnostics, sectionKey, "ZONE_LOCATION_ZONE", $"Step {step.Number} zone", step.Location.Zone, allowProposed: false);
            return;
        }

        if (step.HasRandomOptions)
        {
            ValidateRandomOptions(diagnostics, step, sectionKey);
            return;
        }

        if (QuestSpecFactory.KindForStepType(step.Type) != "generic")
            AddReferenceDiagnostic(diagnostics, sectionKey, "STEP_TARGET", $"Step {step.Number} target", step.Target, allowProposed: false);
    }

    private static void ValidateRandomOptions(List<QuestDiagnostic> diagnostics, QuestStepSpec step, string sectionKey)
    {
        for (var i = 0; i < step.RandomOptions.Count; i++)
        {
            var option = step.RandomOptions[i];
            var optionKey = $"{sectionKey}.option:{i + 1}";
            if (string.IsNullOrWhiteSpace(option.Description))
                Add(diagnostics, QuestDiagnosticSeverity.Blocker, optionKey, "STEP_OPTION_TEXT", $"Step {step.Number} random option {i + 1} has no description.");
            if (option.QuantityMax <= 0)
                Add(diagnostics, QuestDiagnosticSeverity.Blocker, optionKey, "STEP_OPTION_QUANTITY", $"Step {step.Number} random option {i + 1} quantity must be greater than zero.");
            if (QuestSpecFactory.KindForStepType(step.Type) != "generic")
                AddReferenceDiagnostic(diagnostics, optionKey, "STEP_OPTION_TARGET", $"Step {step.Number} random option {i + 1} target", option.Target, allowProposed: false);
        }
    }

    private static void AddReferenceDiagnostic(
        List<QuestDiagnostic> diagnostics,
        string sectionKey,
        string code,
        string label,
        ResolvedReference reference,
        bool allowProposed)
    {
        if (reference.Status == ResolveStatus.Resolved)
            return;
        if (allowProposed && reference.Status == ResolveStatus.Proposed && reference.Id.HasValue)
            return;

        var severity = reference.Status switch
        {
            ResolveStatus.Ambiguous => QuestDiagnosticSeverity.Blocker,
            ResolveStatus.Missing => QuestDiagnosticSeverity.Blocker,
            ResolveStatus.Proposed when !allowProposed => QuestDiagnosticSeverity.Blocker,
            _ => QuestDiagnosticSeverity.Warning
        };
        Add(diagnostics, severity, sectionKey, code, $"{label} is {reference.Status.ToString().ToLowerInvariant()} for query '{reference.Query}'.");
    }

    private static void Add(List<QuestDiagnostic> diagnostics, QuestDiagnosticSeverity severity, string sectionKey, string code, string message)
    {
        diagnostics.Add(new QuestDiagnostic
        {
            Severity = severity,
            SectionKey = sectionKey,
            Code = code,
            Message = message
        });
    }
}
