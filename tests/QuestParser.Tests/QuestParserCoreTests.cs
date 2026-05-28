using System.Net;
using System.Text.Json;
using QuestParser.Core;

namespace QuestParser.Tests;

public sealed class QuestParserCoreTests
{
    [Fact]
    public void CensusQuestFixtureParsesQuestAndQuestGiver()
    {
        var quest = JsonSerializer.Deserialize<CensusQuestResponse>(SampleQuestJson(), JsonOptions())!;
        var givers = JsonSerializer.Deserialize<CensusQuestGiverResponse>(SampleQuestGiverJson(), JsonOptions())!;

        Assert.Equal(1, quest.Returned);
        Assert.Equal("A Hunter's Tool", quest.QuestList[0].Name);
        Assert.Equal(2, quest.QuestList[0].StageList.Count);
        Assert.Equal(3, quest.QuestList[0].StageList[0].BranchList.Count);
        Assert.Single(givers.QuestGiverList);
        Assert.Equal("J.P. Feterman", givers.QuestGiverList[0].Name);
    }

    [Fact]
    public void CensusClientCanBuildRemoteMirrorUrlsWithoutServiceIdPath()
    {
        var uri = CensusClient.BuildQuestUri("A Hunter's Tool", "https://mirror.example", includeServiceId: false);

        Assert.Equal("/get/eq2/quest", uri.AbsolutePath);
        Assert.Equal("mirror.example", uri.Host);
        Assert.DoesNotContain("s:example", uri.ToString());
        Assert.Contains("name=A+Hunter%27s+Tool", uri.Query);
    }

    [Fact]
    public async Task LocalCensusClientReadsDownloadedJsonAndCachesRawPayloads()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "eq2-questparser-local-census-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(tempRoot, "source");
        var cache = Path.Combine(tempRoot, "cache");
        Directory.CreateDirectory(source);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(source, CensusClient.QuestJsonFileName("A Hunter's Tool")), SampleQuestJson());
            await File.WriteAllTextAsync(Path.Combine(source, CensusClient.QuestGiverJsonFileName("A Hunter's Tool")), SampleQuestGiverJson());

            var import = await new LocalCensusClient(source, cache).FetchQuestAsync("A Hunter's Tool");

            Assert.Equal("A Hunter's Tool", import.Quest.Name);
            Assert.Equal(["J.P. Feterman"], import.QuestGivers.Select(giver => giver.Name).ToArray());
            Assert.True(File.Exists(Path.Combine(cache, CensusClient.QuestJsonFileName("A Hunter's Tool"))));
            Assert.True(File.Exists(Path.Combine(cache, CensusClient.QuestGiverJsonFileName("A Hunter's Tool"))));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task LocalCensusClientReadsRewardItemDetailsWhenAvailable()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "eq2-questparser-local-census-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(tempRoot, "source");
        var cache = Path.Combine(tempRoot, "cache");
        Directory.CreateDirectory(source);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(source, CensusClient.QuestJsonFileName("Slay the Revenant Soldiers")), SampleSelectableRewardQuestJson());
            await File.WriteAllTextAsync(Path.Combine(source, CensusClient.QuestGiverJsonFileName("Slay the Revenant Soldiers")), """{"questgiver_list":[],"returned":0}""");
            await File.WriteAllTextAsync(Path.Combine(source, "item.json"), SampleRewardItemJson());

            var import = await new LocalCensusClient(source, cache).FetchQuestAsync("Slay the Revenant Soldiers");

            Assert.Equal(4, import.RewardItems?.Count);
            Assert.Equal("Band of Unimaginable Power", import.RewardItems?[2309818981].DisplayName);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void SpecFactoryIncludesSelectableRewardItems()
    {
        var quest = JsonSerializer.Deserialize<CensusQuestResponse>(SampleSelectableRewardQuestJson(), JsonOptions())!.QuestList[0];
        var items = JsonSerializer.Deserialize<CensusItemResponse>(SampleRewardItemJson(), JsonOptions())!.ItemList.ToDictionary(item => item.Id);
        var spec = new QuestSpecFactory().Create(
            new CensusQuestImport(quest, [], items),
            Path.Combine(Path.GetTempPath(), "eq2-content-test"),
            "Tester");

        Assert.Equal(4, spec.Rewards.Items.Count);
        Assert.All(spec.Rewards.Items, item => Assert.True(item.IsSelectable));
        Assert.Equal("Band of Unimaginable Power", spec.Rewards.Items[0].Item.Query);
        Assert.Equal(ResolveStatus.Missing, spec.Rewards.Items[0].Item.Status);
        Assert.Equal("2309818981", spec.Rewards.Items[0].Item.Metadata["census_id"]);
        Assert.Contains("selectable reward item", spec.Rewards.Items[0].Item.Source);
    }

    [Fact]
    public void SpecFactoryKeepsSelectableRewardIdsWhenItemDetailsAreUnavailable()
    {
        var quest = JsonSerializer.Deserialize<CensusQuestResponse>(SampleSelectableRewardQuestJson(), JsonOptions())!.QuestList[0];
        var spec = new QuestSpecFactory().Create(
            new CensusQuestImport(quest, []),
            Path.Combine(Path.GetTempPath(), "eq2-content-test"),
            "Tester");

        Assert.Equal(4, spec.Rewards.Items.Count);
        Assert.All(spec.Rewards.Items, item => Assert.True(item.IsSelectable));
        Assert.Equal("2309818981", spec.Rewards.Items[0].Item.Query);
        Assert.Contains("item census details not found", spec.Rewards.Items[0].Item.Source);
    }

    [Theory]
    [InlineData("I need to return to J.P. Feterman", StepType.Chat)]
    [InlineData("I must kill five sandstone giants", StepType.Kill)]
    [InlineData("I need to gather maple from wind felled trees", StepType.Harvest)]
    [InlineData("I need to buy some dwarf chunks", StepType.ObtainItem)]
    [InlineData("I need to travel to Oakmyst Forest", StepType.Location)]
    [InlineData("I need to craft a bow", StepType.Craft)]
    public void StepTypeInferenceCoversCommonQuestText(string text, StepType expected)
    {
        Assert.Equal(expected, StepTypeInferer.Infer(text, "", ""));
    }

    [Fact]
    public void SpecFactoryBuildsParallelThenReturnSpec()
    {
        var spec = BuildSampleSpec();

        Assert.Equal("A Hunter's Tool", spec.Quest.Name);
        Assert.Equal("Commonlands", spec.Quest.Zone);
        Assert.Equal(["J.P. Feterman"], spec.QuestGivers);
        Assert.True(spec.Stages[0].IsParallel);
        Assert.False(spec.Stages[1].IsParallel);
        Assert.Equal(4, spec.Stages.SelectMany(stage => stage.Steps).Count());
        Assert.Equal(StepType.Chat, spec.Stages[1].Steps[0].Type);
    }

    [Fact]
    public void SpecFactoryCollapsesCensusRandomKillAlternativesIntoOneStep()
    {
        var spec = BuildRandomKillSpec();

        Assert.Single(spec.Stages);
        Assert.False(spec.Stages[0].IsParallel);
        var step = Assert.Single(spec.Stages[0].Steps);
        Assert.Equal(StepType.Kill, step.Type);
        Assert.Equal(1, step.Number);
        Assert.Equal(3, step.RandomOptions.Count);
        Assert.Equal(["stone beetles", "Bloodskull priests", "dervish cutthroats"], step.RandomOptions.Select(option => option.SearchText).ToArray());
    }

    [Fact]
    public async Task FakeResolverAppliesResolvedAmbiguousMissingAndProposedStates()
    {
        var spec = BuildSampleSpec();
        await new FakeResolver().ResolveAsync(spec);

        Assert.Equal(ResolveStatus.Proposed, spec.QuestId.Status);
        Assert.Equal(9001, spec.QuestId.Id);
        Assert.Equal(ResolveStatus.Resolved, spec.Giver.Status);
        Assert.Contains(spec.Stages.SelectMany(stage => stage.Steps), step => step.Target.Status == ResolveStatus.Ambiguous);
        Assert.Contains(spec.Stages.SelectMany(stage => stage.Steps), step => step.Target.Status == ResolveStatus.Missing);
    }

    [Fact]
    public async Task MissingResolverLeavesReferencesUnresolvedWithoutThrowing()
    {
        var spec = BuildSampleSpec();

        await new MissingQuestDatabaseResolver("Database connection is not configured.").ResolveAsync(spec);

        Assert.Equal(ResolveStatus.Missing, spec.QuestId.Status);
        Assert.Equal("Database connection is not configured.", spec.QuestId.Source);
        Assert.Contains(spec.Todos, todo => todo.Contains("Database connection is not configured.", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(spec.Todos, todo => todo.Contains("Step 1 target", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MissingResolverDoesNotOverwriteResolvedIds()
    {
        var spec = BuildResolvedSpec();
        var originalQuestId = spec.QuestId.Id;
        var originalTargetId = spec.Stages[0].Steps[0].Target.Id;

        await new MissingQuestDatabaseResolver("Database connection is not configured.").ResolveAsync(spec);

        Assert.Equal(originalQuestId, spec.QuestId.Id);
        Assert.Equal(ResolveStatus.Proposed, spec.QuestId.Status);
        Assert.Equal(originalTargetId, spec.Stages[0].Steps[0].Target.Id);
        Assert.Equal(ResolveStatus.Resolved, spec.Stages[0].Steps[0].Target.Status);
    }

    [Fact]
    public void LuaGeneratorEscapesStringsAndGeneratesParallelProgress()
    {
        var spec = BuildResolvedSpec();
        spec.Stages[0].Steps[0].Description = "Gather \"maple\" from trees";
        var lua = new LuaGenerator().Generate(spec);

        Assert.Contains("local AHuntersTool = 9001", lua);
        Assert.Contains("CheckProgressStage1", lua);
        Assert.Contains("QuestStepIsComplete(Player, AHuntersTool, 1)", lua);
        Assert.Contains("Gather \\\"maple\\\" from trees", lua);
        Assert.Contains("Static quest rewards are generated in quest_details SQL", lua);
        Assert.DoesNotContain("AddQuestRewardCoin", lua);
    }

    [Fact]
    public void LuaGeneratorGeneratesOneRandomizedQuestStepForRandomOptions()
    {
        var spec = BuildRandomKillSpec();
        spec.QuestId = ResolvedReference.Resolved("quest", spec.Quest.Name, 6288, spec.Quest.Name);
        spec.Giver = ResolvedReference.Resolved("npc", "wanted poster", 331030, "wanted poster");
        var step = spec.Stages[0].Steps[0];
        var targetIds = new[] { 330070, 330249, 330092 };
        for (var i = 0; i < step.RandomOptions.Count; i++)
            step.RandomOptions[i].Target = ResolvedReference.Resolved("npc", step.RandomOptions[i].SearchText, targetIds[i], step.RandomOptions[i].SearchText);

        var lua = new LuaGenerator().Generate(spec);

        Assert.Contains("local choice = MakeRandomInt(1, 3)", lua);
        Assert.Contains("if choice == 1 then", lua);
        Assert.Contains("elseif choice == 2 then", lua);
        Assert.Contains("AddQuestStepKill(Quest, 1, \"I need to kill stone beetles in the Commonlands\", 15, 100", lua);
        Assert.Contains("AddQuestStepKill(Quest, 1, \"I need to kill Bloodskull priests in the Commonlands\", 15, 100", lua);
        Assert.Contains("AddQuestStepKill(Quest, 1, \"I need to kill dervish cutthroats in the Commonlands\", 15, 100", lua);
        Assert.Contains("AddQuestStepCompleteAction(Quest, 1, \"Step1Complete\")", lua);
        Assert.DoesNotContain("Step2Complete", lua);
        Assert.DoesNotContain("CheckProgressStage1", lua);
    }

    [Fact]
    public void LuaGeneratorUsesMakeRandomIntForQuantityRanges()
    {
        var spec = new QuestSpec
        {
            Quest = new QuestMetadata { Name = "Random Quantity", Zone = "Commonlands", CompletionText = "Done." },
            QuestId = ResolvedReference.Resolved("quest", "Random Quantity", 9002, "Random Quantity"),
            Stages =
            [
                new QuestStageSpec
                {
                    Number = 1,
                    Description = "Kill a random amount.",
                    CompletedDescription = "Killed enough.",
                    Steps =
                    [
                        new QuestStepSpec
                        {
                            Number = 1,
                            Type = StepType.Kill,
                            Description = "Kill tier 2 creatures",
                            CompletedDescription = "Killed tier 2 creatures.",
                            QuantityMin = 15,
                            QuantityMax = 20,
                            IconId = 611,
                            Target = ResolvedReference.Resolved("npc", "tier 2 creatures", 330070, "a stone beetle")
                        }
                    ]
                }
            ]
        };

        var lua = new LuaGenerator().Generate(spec);

        Assert.Contains("AddQuestStepKill(Quest, 1, \"Kill tier 2 creatures\", MakeRandomInt(15, 20), 100", lua);
    }

    [Fact]
    public void SpawnScriptGeneratorCreatesFlexibleQuestStarterExample()
    {
        var spec = BuildResolvedSpec();

        var lua = new SpawnScriptGenerator().Generate(spec);

        Assert.Contains("SpawnScripts/Commonlands/JPFeterman.example.lua", lua);
        Assert.Contains("Suggested live spawn script\t:\tSpawnScripts/Commonlands/JPFeterman.lua", lua);
        Assert.Contains("local AHuntersTool = 9001", lua);
        Assert.Contains("ProvidesQuest(NPC, AHuntersTool)", lua);
        Assert.Contains("function hailed(NPC, Spawn)", lua);
        Assert.Contains("function casted_on(Target, Caster, SpellName)", lua);
        Assert.Contains("function used(NPC, Spawn, SpellName)", lua);
        Assert.Contains("function examined(NPC, Spawn)", lua);
        Assert.Contains("OfferQuest(nil, Player, AHuntersTool)", lua);
        Assert.Contains("CanOfferAHuntersTool", lua);
    }

    [Fact]
    public void SqlGeneratorWritesProposedQuestAndMissingTemplates()
    {
        var spec = BuildResolvedSpec();
        spec.Stages[0].Steps[2].Target = ResolvedReference.Missing("npc", "dervish cutthroat");

        var sql = new SqlReportGenerator().GenerateSql(spec);
        var report = new SqlReportGenerator().GenerateMissingReport(spec);

        Assert.Contains("INSERT INTO quests", sql);
        Assert.Contains("INSERT IGNORE INTO quest_details", sql);
        Assert.Contains("'Coin', 1607", sql);
        Assert.Contains("'MaxCoin', 2191", sql);
        Assert.Contains("'Experience', 153", sql);
        Assert.Contains("-- Missing NPC: dervish cutthroat", sql);
        Assert.Contains("Step 3 Kill", report);
    }

    [Fact]
    public void SqlGeneratorKeepsRewardPreviewWhenQuestIdIsAmbiguous()
    {
        var spec = BuildResolvedSpec();
        spec.QuestId = ResolvedReference.Ambiguous("quest", spec.Quest.Name, [
            new ResolveCandidate { Id = 1, Name = spec.Quest.Name, Kind = "quest" },
            new ResolveCandidate { Id = 2, Name = spec.Quest.Name, Kind = "quest" }
        ]);

        var sql = new SqlReportGenerator().GenerateSql(spec);

        Assert.Contains("Reward preview", sql);
        Assert.Contains("'Coin', 1607", sql);
        Assert.Contains("'MaxCoin', 2191", sql);
        Assert.Contains("'Experience', 153", sql);
    }

    [Fact]
    public void UtilityNormalizesPathsAndSplitsCoin()
    {
        Assert.Equal("a_hunters_tool.lua", Utilities.NormalizeQuestFileName("A Hunter's Tool"));
        Assert.Equal("JPFeterman.example.lua", Utilities.NormalizeSpawnScriptExampleFileName("J.P. Feterman"));
        Assert.Equal((91, 21, 0, 0), Utilities.SplitCoin(2191));
    }

    [Fact]
    public void ManualTemplateCreatesEditableDraftWithProvenanceAndDiagnostics()
    {
        var spec = new QuestTemplateFactory().Create(
            QuestTemplateKind.KillNpc,
            "Cull the Raiders",
            "Commonlands",
            Path.Combine(Path.GetTempPath(), "eq2-content-test"),
            "Tester");

        Assert.Equal("Cull the Raiders", spec.Quest.Name);
        Assert.Single(spec.Stages[0].Steps);
        Assert.Equal(StepType.Kill, spec.Stages[0].Steps[0].Type);
        Assert.Contains("Manual template", spec.Provenance["step.1.type"]);

        var diagnostics = QuestSpecValidator.Validate(spec, overwrite: true);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Severity == QuestDiagnosticSeverity.Blocker && diagnostic.Code == "QUEST_ID");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Severity == QuestDiagnosticSeverity.Blocker && diagnostic.Code == "STEP_TARGET");
    }

    [Fact]
    public void MissingSpawnTemplateIsReviewOnlyAndIncludesScriptPath()
    {
        var spec = BuildSampleSpec();
        var template = MissingSpawnTemplateBuilder.Build(spec, ResolvedReference.Missing("npc", "dervish cutthroat"), "Step 3");
        var text = MissingSpawnTemplateBuilder.Format(template);

        Assert.Equal("dervish cutthroat", template.NpcName);
        Assert.Contains("SpawnScripts/Commonlands/DervishCutthroat.lua", template.SuggestedSpawnScriptPath);
        Assert.Contains("-- INSERT INTO spawn", text);
        Assert.DoesNotContain("\nINSERT INTO spawn", text);
    }

    private static QuestSpec BuildSampleSpec()
    {
        var quest = JsonSerializer.Deserialize<CensusQuestResponse>(SampleQuestJson(), JsonOptions())!.QuestList[0];
        var givers = JsonSerializer.Deserialize<CensusQuestGiverResponse>(SampleQuestGiverJson(), JsonOptions())!.QuestGiverList;
        return new QuestSpecFactory().Create(new CensusQuestImport(quest, givers), Path.Combine(Path.GetTempPath(), "eq2-content-test"), "Tester");
    }

    private static QuestSpec BuildRandomKillSpec()
    {
        var quest = JsonSerializer.Deserialize<CensusQuestResponse>(SampleRandomKillQuestJson(), JsonOptions())!.QuestList[0];
        var givers = JsonSerializer.Deserialize<CensusQuestGiverResponse>(SampleRandomKillQuestGiverJson(), JsonOptions())!.QuestGiverList;
        return new QuestSpecFactory().Create(new CensusQuestImport(quest, givers), Path.Combine(Path.GetTempPath(), "eq2-content-test"), "Tester");
    }

    private static QuestSpec BuildResolvedSpec()
    {
        var spec = BuildSampleSpec();
        new FakeResolver().ResolveAsync(spec).GetAwaiter().GetResult();
        foreach (var step in spec.Stages.SelectMany(stage => stage.Steps))
        {
            if (step.Target.Status != ResolveStatus.Resolved && step.Target.Status != ResolveStatus.Proposed)
                step.Target = ResolvedReference.Resolved(QuestSpecFactory.KindForStepType(step.Type), step.SearchText, 1000 + step.Number, step.SearchText);
        }

        return spec;
    }

    internal static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };

    internal static string SampleQuestJson() => """
        {
          "quest_list": [
            {
              "category": "Commonlands",
              "name": "A Hunter's Tool",
              "level": 17,
              "scales_with_level": 0,
              "is_tradeskill": 0,
              "crc": 2965556880,
              "completion_text": "I gathered the components that J.P. Feterman needed to reconstruct his bow.",
              "shareable": 1,
              "starter_text": "J.P. Feterman asked me to gather some components to help him rebuild his favorite bow.",
              "complete_shareable": 1,
              "tier": 6,
              "repeatable": 0,
              "id": 2965556880,
              "stage_list": [
                {
                  "num": 0,
                  "starter_text_list": ["J.P. Feterman has asked me to gather some components for him to fix his favorite bow."],
                  "completion_text_list": ["I have gathered the components and should return to J.P. Feterman."],
                  "branch_list": [
                    { "quota_min": -1, "description": "I need to gather maple from wind felled trees", "quota_max": 5, "completion_zone": "zones/commonlands", "completed_text": "I have gathered maple for J.P. Feterman.", "icon_name": "Piece of Maple", "icon_id": 824 },
                    { "quota_min": -1, "description": "I need to gather tuber strands from desert roots", "quota_max": 3, "completion_zone": "zones/commonlands", "completed_text": "I have J.P. Feterman's tuber strands.", "icon_name": "Tuber Strands", "icon_id": 194 },
                    { "quota_min": -1, "description": "I need compound bow cams from dervish cutthroats", "quota_max": 2, "completion_zone": "zones/commonlands", "completed_text": "I have the bow cams for J.P. Feterman.", "icon_name": "Compound Bow Cam", "icon_id": 2279 }
                  ]
                },
                {
                  "num": 1,
                  "starter_text_list": ["I have J.P. Feterman's bow components and should speak with him again."],
                  "completion_text_list": ["I have spoken with J.P. Feterman."],
                  "branch_list": [
                    { "quota_min": -1, "description": "I need to return to J.P. Feterman", "quota_max": 1, "completion_zone": "zones/commonlands", "completed_text": "I have given J.P. Feterman his bow.", "icon_name": "", "icon_id": 9 }
                  ]
                }
              ],
              "reward_list": [
                { "coin_min": 1607, "coin_max": 2191, "exp": 153.125925, "item_list": [], "factionchange_list": [] }
              ]
            }
          ],
          "returned": 1
        }
        """;

    internal static string SampleQuestGiverJson() => """
        {
          "questgiver_list": [
            { "name": "J.P. Feterman", "quest_list": [{ "id": 2965556880 }], "id": 3491393786 }
          ],
          "returned": 1
        }
        """;

    internal static string SampleRandomKillQuestJson() => """
        {
          "quest_list": [
            {
              "category": "Commonlands",
              "name": "By Decree of the Overlord",
              "level": 17,
              "scales_with_level": 0,
              "is_tradeskill": 0,
              "crc": 2491014808,
              "completion_text": "I have done my duty for the Overlord.",
              "shareable": 1,
              "starter_text": "I found a poster with orders by decree of the Overlord to vanquish a threat that's poised to ruin our lands.",
              "complete_shareable": 1,
              "tier": 4,
              "repeatable": 1,
              "id": 2491014808,
              "stage_list": [
                {
                  "num": 0,
                  "starter_text_list": ["The Overlord condemns these creatures, and I shall carry out his wishes."],
                  "completion_text_list": ["I have done as the Overlord has commanded."],
                  "branch_list": [
                    { "quota_min": 15, "description": "I need to kill stone beetles in the Commonlands", "quota_max": 15, "completion_zone": "zones/commonlands", "completed_text": "I have killed the stone beetles.", "icon_name": "", "icon_id": 611 },
                    { "quota_min": 15, "description": "I need to kill Bloodskull priests in the Commonlands", "quota_max": 15, "completion_zone": "zones/commonlands", "completed_text": "I have killed the Bloodskull priests.", "icon_name": "", "icon_id": 611 },
                    { "quota_min": 15, "description": "I need to kill dervish cutthroats in the Commonlands", "quota_max": 15, "completion_zone": "zones/commonlands", "completed_text": "I have killed the dervish cutthroats.", "icon_name": "", "icon_id": 611 }
                  ]
                }
              ],
              "reward_list": [
                { "coin_min": 1594, "coin_max": 1604, "exp": 153.125925, "item_list": [], "factionchange_list": [] }
              ]
            }
          ],
          "returned": 1
        }
        """;

    internal static string SampleRandomKillQuestGiverJson() => """
        {
          "questgiver_list": [
            { "name": "wanted poster", "quest_list": [{ "id": 2491014808 }], "id": 331030 }
          ],
          "returned": 1
        }
        """;

    internal static string SampleSelectableRewardQuestJson() => """
        {
          "quest_list": [
            {
              "category": "Thundering Steppes",
              "name": "Slay the Revenant Soldiers",
              "level": 23,
              "scales_with_level": 0,
              "is_tradeskill": 0,
              "crc": 3638274690,
              "completion_text": "I have slain the revenant soldiers.",
              "shareable": 1,
              "starter_text": "I found a journal.",
              "complete_shareable": 1,
              "tier": 9,
              "repeatable": 0,
              "id": 3638274690,
              "stage_list": [
                {
                  "num": 0,
                  "starter_text_list": ["I need to slay them."],
                  "completion_text_list": ["I have slain them."],
                  "branch_list": [
                    { "quota_min": -1, "description": "I need to slay fifteen revenant soldiers in the Thundering Steppes.", "quota_max": 15, "completion_zone": "zones/steppes", "completed_text": "I have slain the revenant soldiers.", "icon_name": "", "icon_id": 611 }
                  ]
                }
              ],
              "reward_list": [
                {
                  "coin_min": 11053,
                  "coin_max": 9765,
                  "exp": 358.913591,
                  "item_list": [],
                  "selected_item_list": [
                    { "id": 2309818981, "quantity": 1 },
                    { "id": 1375427748, "quantity": 1 },
                    { "id": 3635146300, "quantity": 1 },
                    { "id": 4048671300, "quantity": 1 }
                  ],
                  "factionchange_list": []
                }
              ]
            }
          ],
          "returned": 1
        }
        """;

    internal static string SampleRewardItemJson() => """
        {
          "item_list": [
            { "itemlevel": 20, "visible": 1, "displayname": "Band of Unimaginable Power", "id": 2309818981 },
            { "itemlevel": 20, "visible": 1, "displayname": "Loop of Unimaginable Power", "id": 1375427748 },
            { "itemlevel": 20, "visible": 1, "displayname": "Ringlet of Unimaginable Power", "id": 3635146300 },
            { "itemlevel": 20, "visible": 1, "displayname": "Ring of Unimaginable Power", "id": 4048671300 }
          ],
          "returned": 4
        }
        """;

    private sealed class FakeResolver : IQuestDatabaseResolver
    {
        public Task ResolveAsync(QuestSpec spec, CancellationToken cancellationToken = default)
        {
            spec.QuestId = ResolvedReference.Proposed("quest", spec.Quest.Name, 9001, spec.Quest.Name);
            spec.Giver = ResolvedReference.Resolved("npc", "J.P. Feterman", 331133, "J.P. Feterman");

            foreach (var step in spec.Stages.SelectMany(stage => stage.Steps))
            {
                step.Target = step.Number switch
                {
                    1 => ResolvedReference.Resolved("item", step.SearchText, 1001001, step.SearchText),
                    2 => ResolvedReference.Ambiguous("item", step.SearchText, [
                        new ResolveCandidate { Id = 1001002, Name = "Tuber Strands", Kind = "item" },
                        new ResolveCandidate { Id = 1001003, Name = "Tuber Strands", Kind = "item" }
                    ]),
                    3 => ResolvedReference.Missing("npc", step.SearchText),
                    4 => spec.Giver,
                    _ => ResolvedReference.Missing("unknown", step.SearchText)
                };
            }

            spec.Todos = ["Step 2 target: ambiguous 'Tuber Strands'.", "Step 3 target: missing 'Compound Bow Cam'."];
            return Task.CompletedTask;
        }
    }
}
