namespace QuestParser.Core;

public sealed class QuestTemplateFactory
{
    public QuestSpec Create(
        QuestTemplateKind kind,
        string questName,
        string zone,
        string? contentRoot = null,
        string author = "")
    {
        var cleanQuestName = string.IsNullOrWhiteSpace(questName) ? "New Quest" : questName.Trim();
        var cleanZone = string.IsNullOrWhiteSpace(zone) ? "Uncategorized" : zone.Trim();
        var resolvedContentRoot = contentRoot ?? Defaults.ContentRoot;
        var spec = new QuestSpec
        {
            Quest = new QuestMetadata
            {
                Name = cleanQuestName,
                Zone = cleanZone,
                Author = author,
                StarterText = "TODO: Add quest offer text.",
                CompletionText = "TODO: Add quest completion text."
            },
            QuestId = ResolvedReference.Missing("quest", cleanQuestName),
            Giver = ResolvedReference.Missing("npc", "TODO quest giver"),
            Output = QuestSpecFactory.BuildOutputPaths(resolvedContentRoot, cleanZone, cleanQuestName, "TODO quest giver")
        };

        spec.QuestGivers.Add("TODO quest giver");
        ApplyTemplate(spec, kind);
        AddTemplateProvenance(spec, kind);
        return spec;
    }

    public static string DisplayName(QuestTemplateKind kind)
    {
        return kind switch
        {
            QuestTemplateKind.SpeakToNpc => "Speak to NPC",
            QuestTemplateKind.KillNpc => "Kill NPC",
            QuestTemplateKind.CollectItem => "Collect Item",
            QuestTemplateKind.Harvest => "Harvest",
            QuestTemplateKind.Craft => "Craft",
            QuestTemplateKind.VisitLocation => "Visit Location",
            _ => "Blank"
        };
    }

    private static void ApplyTemplate(QuestSpec spec, QuestTemplateKind kind)
    {
        var stage = new QuestStageSpec
        {
            Number = 1,
            Description = "TODO: Add task group text.",
            CompletedDescription = "TODO: Add completed task group text."
        };

        if (kind == QuestTemplateKind.Blank)
        {
            spec.Stages.Add(stage);
            return;
        }

        stage.Steps.Add(kind switch
        {
            QuestTemplateKind.SpeakToNpc => CreateStep(1, StepType.Chat, "Speak with TODO npc", "Spoke with TODO npc.", "TODO npc", 1, iconId: 9),
            QuestTemplateKind.KillNpc => CreateStep(1, StepType.Kill, "Kill TODO npc", "Killed TODO npc.", "TODO npc", 1, iconId: 4),
            QuestTemplateKind.CollectItem => CreateStep(1, StepType.ObtainItem, "Collect TODO item", "Collected TODO item.", "TODO item", 1, iconId: 2),
            QuestTemplateKind.Harvest => CreateStep(1, StepType.Harvest, "Harvest TODO resource", "Harvested TODO resource.", "TODO resource", 1, iconId: 2),
            QuestTemplateKind.Craft => CreateStep(1, StepType.Craft, "Craft TODO item", "Crafted TODO item.", "TODO item", 1, iconId: 2),
            QuestTemplateKind.VisitLocation => new QuestStepSpec
            {
                Number = 1,
                Type = StepType.Location,
                Description = "Visit TODO location",
                CompletedDescription = "Visited TODO location.",
                QuantityMax = 1,
                Percentage = 100,
                IconId = 7,
                SearchText = "TODO location",
                Target = ResolvedReference.Missing("location", "TODO location"),
                Location = new LocationTarget
                {
                    Zone = ResolvedReference.Missing("zone", spec.Quest.Zone)
                }
            },
            _ => CreateStep(1, StepType.Generic, "TODO quest step", "TODO step complete.", "", 1)
        });
        spec.Stages.Add(stage);
    }

    private static QuestStepSpec CreateStep(int number, StepType type, string description, string completed, string searchText, int quantity, int iconId = 0)
    {
        return new QuestStepSpec
        {
            Number = number,
            Type = type,
            Description = description,
            CompletedDescription = completed,
            QuantityMax = quantity,
            Percentage = 100,
            IconId = iconId,
            SearchText = searchText,
            Target = ResolvedReference.Missing(QuestSpecFactory.KindForStepType(type), searchText)
        };
    }

    private static void AddTemplateProvenance(QuestSpec spec, QuestTemplateKind kind)
    {
        var templateSource = $"Manual template: {DisplayName(kind)}";
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
            "quest.completion",
            "giver.query",
            "output.contentRoot",
            "output.questDirectory",
            "output.lua",
            "output.spec",
            "output.sql",
            "output.missing",
            "output.preview"
        })
        {
            spec.Provenance[key] = templateSource;
        }

        foreach (var step in spec.Stages.SelectMany(stage => stage.Steps))
        {
            spec.Provenance[$"step.{step.Number}.type"] = templateSource;
            spec.Provenance[$"step.{step.Number}.description"] = templateSource;
            spec.Provenance[$"step.{step.Number}.completed"] = templateSource;
            spec.Provenance[$"step.{step.Number}.quantityMax"] = templateSource;
            spec.Provenance[$"step.{step.Number}.iconId"] = templateSource;
            spec.Provenance[$"step.{step.Number}.searchText"] = templateSource;
        }
    }
}
