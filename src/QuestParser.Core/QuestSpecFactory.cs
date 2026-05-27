namespace QuestParser.Core;

public sealed class QuestSpecFactory
{
    public QuestSpec Create(CensusQuestImport import, string? contentRoot = null, string author = "")
    {
        var quest = import.Quest;
        var resolvedContentRoot = contentRoot ?? Defaults.ContentRoot;
        var primaryGiver = import.QuestGivers.FirstOrDefault(g => !string.IsNullOrWhiteSpace(g.Name))?.Name ?? "";
        var spec = new QuestSpec
        {
            Quest = new QuestMetadata
            {
                Name = quest.Name,
                Zone = quest.Category,
                Level = quest.Level,
                Tier = quest.Tier,
                Repeatable = quest.Repeatable == 1,
                Shareable = quest.Shareable == 1,
                CompleteShareable = quest.CompleteShareable == 1,
                IsTradeskill = quest.IsTradeskill == 1,
                ScalesWithLevel = quest.ScalesWithLevel == 1,
                CensusId = quest.Id,
                CensusCrc = quest.Crc,
                StarterText = quest.StarterText,
                CompletionText = quest.CompletionText,
                Author = author
            },
            QuestGivers = import.QuestGivers
                .Where(g => !string.IsNullOrWhiteSpace(g.Name))
                .Select(g => g.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Giver = string.IsNullOrWhiteSpace(primaryGiver)
                ? ResolvedReference.Missing("npc", "")
                : ResolvedReference.Missing("npc", primaryGiver),
            Output = BuildOutputPaths(resolvedContentRoot, quest.Category, quest.Name)
        };
        AddQuestProvenance(spec);

        var stepNumber = 1;
        foreach (var stage in quest.StageList.OrderBy(s => s.Number))
        {
            var isRandomAlternativeStage = IsRandomAlternativeStage(stage);
            var stageSpec = new QuestStageSpec
            {
                Number = spec.Stages.Count + 1,
                Description = stage.StarterTextList.FirstOrDefault() ?? "",
                CompletedDescription = stage.CompletionTextList.FirstOrDefault() ?? "",
                IsParallel = stage.BranchList.Count > 1 && !isRandomAlternativeStage
            };

            if (isRandomAlternativeStage)
            {
                var firstBranch = stage.BranchList[0];
                var step = CreateStep(stepNumber++, firstBranch, primaryGiver);
                step.CompletedDescription = string.IsNullOrWhiteSpace(stageSpec.CompletedDescription)
                    ? firstBranch.CompletedText
                    : stageSpec.CompletedDescription;
                step.RandomOptions = stage.BranchList
                    .Select(branch => CreateRandomOption(branch, step.Type, primaryGiver))
                    .ToList();
                step.QuantityMin = step.RandomOptions
                    .Where(option => option.QuantityMin > 0)
                    .Select(option => option.QuantityMin)
                    .DefaultIfEmpty(step.QuantityMin)
                    .Min();
                step.QuantityMax = step.RandomOptions
                    .Select(option => option.QuantityMax <= 0 ? 1 : option.QuantityMax)
                    .DefaultIfEmpty(step.QuantityMax)
                    .Max();
                step.Target = ResolvedReference.Missing(KindForStepType(step.Type), string.Join(" | ", step.RandomOptions.Select(option => option.SearchText)));
                stageSpec.Steps.Add(step);
                AddStepProvenance(spec, step, "Census random branch set");
                spec.Provenance[$"step.{step.Number}.randomOptions"] = "Census random branch set";
            }
            else
            {
                foreach (var branch in stage.BranchList)
                {
                    var step = CreateStep(stepNumber++, branch, primaryGiver);
                    stageSpec.Steps.Add(step);
                    AddStepProvenance(spec, step, "Census stage branch");
                }
            }

            spec.Stages.Add(stageSpec);
        }

        var reward = quest.RewardList.FirstOrDefault();
        if (reward is not null)
        {
            spec.Rewards.CoinMin = reward.CoinMin;
            spec.Rewards.CoinMax = reward.CoinMax;
            spec.Rewards.Experience = reward.Experience;
            spec.Rewards.Items = reward.ItemList.Select(item => new RewardItemSpec
            {
                Quantity = item.Quantity <= 0 ? 1 : item.Quantity,
                Item = item.Id > 0
                    ? ResolvedReference.Resolved("item", item.Name, item.Id, item.Name, source: "Census reward item id")
                    : ResolvedReference.Missing("item", item.Name)
            }).ToList();
            spec.Rewards.Factions = reward.FactionChangeList.Select(faction => new RewardFactionSpec
            {
                Amount = faction.Amount,
                Faction = faction.Id > 0
                    ? ResolvedReference.Resolved("faction", faction.Name, faction.Id, faction.Name, source: "Census reward faction id")
                    : ResolvedReference.Missing("faction", faction.Name)
            }).ToList();
            AddRewardProvenance(spec);
        }

        return spec;
    }

    public static OutputPaths BuildOutputPaths(string contentRoot, string zone, string questName)
    {
        var questDirectory = Path.Combine(contentRoot, "Quests", Utilities.SafeDirectoryName(zone));
        var luaFile = Utilities.NormalizeQuestFileName(questName);
        var specFile = Utilities.NormalizeSpecFileName(questName);
        return new OutputPaths
        {
            ContentRoot = contentRoot,
            QuestDirectory = questDirectory,
            LuaPath = Path.Combine(questDirectory, luaFile),
            SpecPath = Path.Combine(questDirectory, specFile),
            SqlPath = Path.Combine(questDirectory, Utilities.NormalizeSqlFileName(questName)),
            MissingReportPath = Path.Combine(questDirectory, Utilities.NormalizeMissingReportFileName(questName)),
            PreviewPath = Utilities.RuntimePath("output", "preview", luaFile)
        };
    }

    public static string KindForStepType(StepType stepType)
    {
        return stepType switch
        {
            StepType.Chat or StepType.Kill => "npc",
            StepType.KillByRace => "race",
            StepType.Harvest or StepType.ObtainItem or StepType.Craft => "item",
            StepType.Spell => "spell",
            StepType.Location or StepType.ZoneLocation => "location",
            _ => "generic"
        };
    }

    private static bool IsRandomAlternativeStage(CensusStage stage)
    {
        if (stage.BranchList.Count <= 1)
            return false;
        if (stage.BranchList.Any(branch => branch.QuantityMin < 0))
            return false;

        var types = stage.BranchList
            .Select(branch => StepTypeInferer.Infer(branch.Description, branch.CompletedText, branch.IconName))
            .Distinct()
            .ToArray();

        return types.Length == 1 && types[0] is StepType.Kill or StepType.KillByRace;
    }

    private static QuestStepSpec CreateStep(int number, CensusBranch branch, string primaryGiver)
    {
        var stepType = StepTypeInferer.Infer(branch.Description, branch.CompletedText, branch.IconName);
        var searchText = StepTypeInferer.InferSearchText(stepType, branch.Description, branch.IconName, primaryGiver);
        return new QuestStepSpec
        {
            Number = number,
            Type = stepType,
            Description = branch.Description,
            CompletedDescription = branch.CompletedText,
            QuantityMin = branch.QuantityMin,
            QuantityMax = branch.QuantityMax <= 0 ? 1 : branch.QuantityMax,
            IconId = branch.IconId,
            IconName = branch.IconName,
            CompletionZone = string.IsNullOrWhiteSpace(branch.CompletionZoneOverride) ? branch.CompletionZone : branch.CompletionZoneOverride,
            SearchText = searchText,
            Target = ResolvedReference.Missing(KindForStepType(stepType), searchText)
        };
    }

    private static QuestStepOptionSpec CreateRandomOption(CensusBranch branch, StepType stepType, string primaryGiver)
    {
        var searchText = StepTypeInferer.InferSearchText(stepType, branch.Description, branch.IconName, primaryGiver);
        return new QuestStepOptionSpec
        {
            Description = branch.Description,
            CompletedDescription = branch.CompletedText,
            QuantityMin = branch.QuantityMin,
            QuantityMax = branch.QuantityMax <= 0 ? 1 : branch.QuantityMax,
            IconId = branch.IconId,
            IconName = branch.IconName,
            CompletionZone = string.IsNullOrWhiteSpace(branch.CompletionZoneOverride) ? branch.CompletionZone : branch.CompletionZoneOverride,
            SearchText = searchText,
            Target = ResolvedReference.Missing(KindForStepType(stepType), searchText)
        };
    }

    private static void AddQuestProvenance(QuestSpec spec)
    {
        foreach (var key in new[]
        {
            "quest.name",
            "quest.zone",
            "quest.level",
            "quest.tier",
            "quest.repeatable",
            "quest.shareable",
            "quest.completeShareable",
            "quest.tradeskill",
            "quest.scales",
            "quest.starter",
            "quest.completion"
        })
        {
            spec.Provenance[key] = "Census quest";
        }

        spec.Provenance["giver.query"] = "Census questgiver lookup";
        spec.Provenance["output.contentRoot"] = "User/default content root";
        spec.Provenance["output.questDirectory"] = "Generated output path";
        spec.Provenance["output.lua"] = "Generated output path";
        spec.Provenance["output.spec"] = "Generated output path";
        spec.Provenance["output.sql"] = "Generated output path";
        spec.Provenance["output.missing"] = "Generated output path";
        spec.Provenance["output.preview"] = "Generated runtime preview path";
    }

    private static void AddStepProvenance(QuestSpec spec, QuestStepSpec step, string source)
    {
        spec.Provenance[$"step.{step.Number}.type"] = "Generated by StepTypeInferer from Census branch/icon";
        spec.Provenance[$"step.{step.Number}.description"] = source;
        spec.Provenance[$"step.{step.Number}.completed"] = source;
        spec.Provenance[$"step.{step.Number}.quantityMin"] = source;
        spec.Provenance[$"step.{step.Number}.quantityMax"] = source;
        spec.Provenance[$"step.{step.Number}.iconId"] = source;
        spec.Provenance[$"step.{step.Number}.iconName"] = source;
        spec.Provenance[$"step.{step.Number}.completionZone"] = source;
        spec.Provenance[$"step.{step.Number}.searchText"] = "Generated search text from Census branch/icon";
    }

    private static void AddRewardProvenance(QuestSpec spec)
    {
        spec.Provenance["rewards.coinMin"] = "Census reward";
        spec.Provenance["rewards.coinMax"] = "Census reward";
        spec.Provenance["rewards.xp"] = "Census reward";
        spec.Provenance["rewards.items"] = "Census reward";
        spec.Provenance["rewards.factions"] = "Census reward";
    }
}
