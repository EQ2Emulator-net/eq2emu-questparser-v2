using System.Net;
using System.Diagnostics;
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
    public void ModuleLuaGeneratorEmitsStageTablesAndHandlers()
    {
        var spec = BuildResolvedSpec();
        spec.GenerationMode = QuestGenerationMode.ModuleLua;

        var lua = new ModuleLuaGenerator().Generate(spec);

        Assert.Contains("require \"Quests/Generic/QuestModule\"", lua);
        Assert.DoesNotContain("local QuestModule = require", lua);
        Assert.Contains("local STAGE_1_STEPS = {", lua);
        Assert.Contains("local ALL_STEPS = QuestModule.ExportStageStepHandlers({", lua);
        Assert.Contains("\tSTAGE_1_STEPS,", lua);
        Assert.Contains("}, { overwrite = true })", lua);
        Assert.DoesNotContain("QuestModule.ExportStepHandlers(STAGE_", lua);
        Assert.DoesNotContain("for _, step in ipairs(STAGE_", lua);
        Assert.Contains("QuestModule.AddSteps(Quest, STAGE_1_STEPS)", lua);
        Assert.Equal(1, CountOccurrences(lua, "QuestModule.ReloadByStep"));
    }

    [Fact]
    public void ModuleLuaGeneratorUsesQuestModuleAllCompleteForParallelProgress()
    {
        var spec = BuildResolvedSpec();
        spec.GenerationMode = QuestGenerationMode.ModuleLua;

        var lua = new ModuleLuaGenerator().Generate(spec);

        Assert.Contains("\tif QuestModule.AllComplete(Player, AHuntersTool, STAGE_1_STEPS) then", lua);
        Assert.DoesNotContain("QuestStepIsComplete(Player, AHuntersTool, 1) and", lua);
    }

    [Fact]
    public void ModuleLuaGeneratorKeepsLegacyGeneratorUnchanged()
    {
        var spec = BuildResolvedSpec();
        spec.GenerationMode = QuestGenerationMode.ModuleLua;

        var lua = new LuaGenerator().Generate(spec);

        Assert.DoesNotContain("Quests/Generic/QuestModule", lua);
        Assert.Contains("function AddStage1Steps(Quest)", lua);
        Assert.Contains("function Step1Complete(Quest, QuestGiver, Player)", lua);
        Assert.Contains("function Reload(Quest, QuestGiver, Player, Step)", lua);
    }

    [Fact]
    public void ModuleLuaGeneratorUsesLuaSafeQuestIdentifier()
    {
        var spec = BuildResolvedSpec();
        spec.Quest.Name = "123 Training Mission";

        var lua = new ModuleLuaGenerator().Generate(spec);

        Assert.Contains("local Quest123TrainingMission = 9001", lua);
        Assert.DoesNotContain("local 123TrainingMission = 9001", lua);
    }

    [Fact]
    public void ModuleLuaGeneratorEmitsQuestModuleCompatibleTypesAndFields()
    {
        var spec = BuildAllStepTypesSpec();
        var expectedModuleTypes = new Dictionary<StepType, string>
        {
            [StepType.Generic] = "basic",
            [StepType.Chat] = "chat",
            [StepType.Craft] = "craft",
            [StepType.Harvest] = "harvest",
            [StepType.Kill] = "kill",
            [StepType.KillByRace] = "killByRace",
            [StepType.Location] = "location",
            [StepType.ObtainItem] = "obtainItem",
            [StepType.Spell] = "spell",
            [StepType.ZoneLocation] = "zoneLoc"
        };

        var lua = new ModuleLuaGenerator().Generate(spec);

        Assert.Equal(Enum.GetValues<StepType>().OrderBy(type => type), expectedModuleTypes.Keys.OrderBy(type => type));
        foreach (var moduleType in expectedModuleTypes.Values)
            Assert.Contains($"\t\ttype = \"{moduleType}\",", lua);
        Assert.Contains("\t\t\t{ x = 10.125, y = 20.25, z = -30.375 }", lua);
        Assert.Contains("\t\t\t{ x = 40.125, y = 50.25, z = 60.375, zone = 12 }", lua);
        Assert.DoesNotContain("\t\t\t{ x = 0, y = 0, z = 0", lua);
        Assert.DoesNotContain("\t\ttype = \"Harvest\",", lua);
        Assert.DoesNotContain("\t\ttype = \"ObtainItem\",", lua);
        Assert.Contains("\t\ttaskGroupText = ", lua);
        Assert.Contains("\t\ttaskGroupDescription = ", lua);
        Assert.Contains("\t\tcompleteText = ", lua);
        Assert.Contains("\t\tcompleteDescription = ", lua);
        Assert.Contains("\t\tcompleteTaskGroup = ", lua);
        Assert.Contains("\t\tcompleteTaskGroupDescription = ", lua);
    }

    [Fact]
    public void ModuleLuaGeneratorEmitsQuestModuleCompatibleLocations()
    {
        var spec = BuildLocationSpec();

        var lua = new ModuleLuaGenerator().Generate(spec);

        Assert.Contains("\t\ttype = \"location\",", lua);
        Assert.Contains("\t\tmaxVariation = 12.345,", lua);
        Assert.Contains("\t\tlocations = {", lua);
        Assert.Contains("\t\t\t{ x = 1.234, y = 2, z = -3.456 }", lua);
        Assert.DoesNotContain("\t\t\t{ x = 1.234, y = 2, z = -3.456, zone =", lua);
        Assert.Contains("\t\ttype = \"zoneLoc\",", lua);
        Assert.Contains("\t\tmaxVariation = 25,", lua);
        Assert.Contains("\t\t\t{ x = 4, y = 5.5, z = 6, zone = 12 }", lua);
        Assert.DoesNotContain("\t\ttargets =", lua);
    }

    [Fact]
    public void ModuleLuaGeneratorEmitsQuantityRangesAsData()
    {
        var spec = BuildQuantityRangeModuleSpec();

        var lua = new ModuleLuaGenerator().Generate(spec);

        Assert.Contains("\t\tcount = { min = 15, max = 20 },", lua);
        Assert.DoesNotContain("MakeRandomInt(15, 20)", lua);
    }

    [Fact]
    public void ModuleLuaGeneratorEmitsRandomOptions()
    {
        var spec = BuildRandomModuleSpec();

        var lua = new ModuleLuaGenerator().Generate(spec);

        Assert.Contains("\t\tid = 1,", lua);
        Assert.Contains("\t\ttype = \"kill\",", lua);
        Assert.Contains("\t\tcomplete = \"Step1Complete\",", lua);
        Assert.Contains("\t\trandomOptions = {", lua);
        Assert.Contains("\t\t\t\ttext = \"Kill stone beetles\",", lua);
        Assert.Contains("\t\t\t\tcount = { min = 3, max = 5 },", lua);
        Assert.DoesNotContain("MakeRandomInt(3, 5)", lua);
        Assert.Contains("\t\t\t\tpercentage = 100,", lua);
        Assert.Contains("\t\t\t\ticon = 611,", lua);
        Assert.Contains("\t\t\t\ttargets = { 330070 }", lua);
        Assert.Contains("\t\t\t\ttext = \"Kill dervish cutthroats\",", lua);
        Assert.Contains("\t\t\t\ttargets = { 330092 }", lua);
        Assert.DoesNotContain("\t\ttargets = {},", lua);
    }

    [Fact]
    public void ModuleLuaGeneratorOmitsEmptyTargetsForTargetlessBasicStep()
    {
        var spec = BuildTargetlessBasicSpec();

        var lua = new ModuleLuaGenerator().Generate(spec);

        Assert.Contains("\t\ttype = \"basic\",", lua);
        Assert.DoesNotContain("\t\ttargets = {},", lua);
        Assert.DoesNotContain("\t\ttargets =", lua);
    }

    [Fact]
    public void ModuleLuaGeneratorSanitizesHeaderLongCommentTerminators()
    {
        var spec = BuildResolvedSpec();
        spec.Quest.Name = "Bad ]] Quest";
        spec.Quest.Zone = "Zone ]] Name";
        spec.Quest.Author = "Author ]] Name";
        spec.Giver.Name = "Giver ]] Name";

        var lua = new ModuleLuaGenerator().Generate(spec);

        Assert.DoesNotContain("Bad ]] Quest", lua);
        Assert.DoesNotContain("Zone ]] Name", lua);
        Assert.DoesNotContain("Author ]] Name", lua);
        Assert.DoesNotContain("Giver ]] Name", lua);
        Assert.Contains("Bad ] ] Quest", lua);
        Assert.Contains("Zone ] ] Name", lua);
        Assert.Contains("Author ] ] Name", lua);
        Assert.Contains("Giver ] ] Name", lua);
    }

    [Fact]
    public async Task ModuleLuaGeneratorGeneratedLuaRunsWithGlobalQuestModuleStub()
    {
        var luaExecutable = FindLuaExecutable();
        if (luaExecutable is null)
            return;

        var tempRoot = Path.Combine(Path.GetTempPath(), "eq2-questparser-lua-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "Quests", "Generic"));
        try
        {
            var generatedPath = Path.Combine(tempRoot, "generated.lua");
            var smokePath = Path.Combine(tempRoot, "smoke.lua");
            var modulePath = Path.Combine(tempRoot, "Quests", "Generic", "QuestModule.lua");
            await File.WriteAllTextAsync(modulePath, QuestModuleSmokeStubLua());
            await File.WriteAllTextAsync(generatedPath, new ModuleLuaGenerator().Generate(BuildModuleLuaSmokeSpec()));
            await File.WriteAllTextAsync(smokePath, """
                package.path = "./?.lua;./?/init.lua;" .. package.path
                dofile("generated.lua")
                dofile("generated.lua")
                Init({})
                assert(#QuestModule.added == 4, "expected Init to add 4 smoke steps, got " .. tostring(#QuestModule.added))
                print("lua smoke ok")
                """);

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo(luaExecutable)
            {
                WorkingDirectory = tempRoot,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            process.StartInfo.ArgumentList.Add(smokePath);
            process.Start();
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.True(process.ExitCode == 0, stdout + stderr);
            Assert.Contains("lua smoke ok", stdout);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ValidatorWarnsWhenModuleLuaQuestModuleIsMissing()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "eq2-questparser-module-validation-" + Guid.NewGuid().ToString("N"));
        var spec = BuildResolvedSpec();
        spec.GenerationMode = QuestGenerationMode.ModuleLua;
        spec.Output.ContentRoot = tempRoot;

        var diagnostics = QuestSpecValidator.Validate(spec, overwrite: true);

        AssertDiagnostic(
            diagnostics,
            QuestDiagnosticSeverity.Warning,
            "MODULE_LUA_MISSING_QUEST_MODULE",
            "Quests/Generic/QuestModule.lua");
    }

    [Fact]
    public void ValidatorFlagsDuplicateModuleLuaStepIds()
    {
        var spec = BuildResolvedSpec();
        spec.GenerationMode = QuestGenerationMode.ModuleLua;
        spec.Stages[1].Steps[0].Number = spec.Stages[0].Steps[0].Number;

        var diagnostics = QuestSpecValidator.Validate(spec, overwrite: true);

        AssertDiagnostic(
            diagnostics,
            QuestDiagnosticSeverity.Blocker,
            "DUPLICATE_STEP_ID",
            "Step id 1 is used more than once");
    }

    [Fact]
    public void ValidatorFlagsNonContiguousModuleLuaStages()
    {
        var spec = BuildResolvedSpec();
        spec.GenerationMode = QuestGenerationMode.ModuleLua;
        spec.Stages[1].Number = 3;

        var diagnostics = QuestSpecValidator.Validate(spec, overwrite: true);

        AssertDiagnostic(
            diagnostics,
            QuestDiagnosticSeverity.Blocker,
            "MODULE_LUA_STAGE_SEQUENCE",
            "numbered 1 through 2");
    }

    [Fact]
    public void ValidatorFlagsInvalidStepQuantityRange()
    {
        var spec = BuildQuantityRangeModuleSpec();
        spec.GenerationMode = QuestGenerationMode.ModuleLua;
        spec.Stages[0].Steps[0].QuantityMin = 20;
        spec.Stages[0].Steps[0].QuantityMax = 15;

        var diagnostics = QuestSpecValidator.Validate(spec, overwrite: true);

        AssertDiagnostic(
            diagnostics,
            QuestDiagnosticSeverity.Blocker,
            "STEP_QUANTITY_RANGE",
            "minimum quantity 20 is greater than maximum quantity 15");
    }

    [Fact]
    public void ValidatorAllowsLegacyStepQuantityRangeForCompatibility()
    {
        var spec = BuildQuantityRangeModuleSpec();
        spec.GenerationMode = QuestGenerationMode.LegacySpawnStub;
        spec.Stages[0].Steps[0].QuantityMin = 20;
        spec.Stages[0].Steps[0].QuantityMax = 15;

        var diagnostics = QuestSpecValidator.Validate(spec, overwrite: true);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "STEP_QUANTITY_RANGE");
    }

    [Fact]
    public void ValidatorFlagsInvalidRandomOptionQuantityRange()
    {
        var spec = BuildRandomModuleSpec();
        spec.GenerationMode = QuestGenerationMode.ModuleLua;
        spec.Stages[0].Steps[0].RandomOptions[0].QuantityMin = 8;
        spec.Stages[0].Steps[0].RandomOptions[0].QuantityMax = 5;

        var diagnostics = QuestSpecValidator.Validate(spec, overwrite: true);

        AssertDiagnostic(
            diagnostics,
            QuestDiagnosticSeverity.Blocker,
            "STEP_OPTION_QUANTITY_RANGE",
            "minimum quantity 8 is greater than maximum quantity 5");
    }

    [Fact]
    public void ValidatorAllowsLegacyRandomOptionQuantityRangeForCompatibility()
    {
        var spec = BuildRandomModuleSpec();
        spec.GenerationMode = QuestGenerationMode.LegacySpawnStub;
        spec.Stages[0].Steps[0].RandomOptions[0].QuantityMin = 8;
        spec.Stages[0].Steps[0].RandomOptions[0].QuantityMax = 5;

        var diagnostics = QuestSpecValidator.Validate(spec, overwrite: true);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "STEP_OPTION_QUANTITY_RANGE");
    }

    [Fact]
    public async Task GenerateFromSpecAsyncLegacyWritesDespiteUnresolvedTargetBlocker()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "eq2-questparser-legacy-blocker-" + Guid.NewGuid().ToString("N"));
        var spec = BuildWritableWorkflowSpec(tempRoot, QuestGenerationMode.LegacySpawnStub);
        spec.Stages[0].Steps[0].Target = ResolvedReference.Missing("npc", "missing target");
        Directory.CreateDirectory(Path.GetDirectoryName(spec.Output.PreviewPath)!);
        try
        {
            var diagnostics = QuestSpecValidator.Validate(spec, overwrite: true);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "STEP_TARGET");

            await new QuestWorkflow().GenerateFromSpecAsync(spec, overwrite: true);

            Assert.True(File.Exists(spec.Output.LuaPath));
            Assert.True(File.Exists(spec.Output.SpecPath));
            Assert.True(File.Exists(spec.Output.SqlPath));
            Assert.True(File.Exists(spec.Output.MissingReportPath));
            Assert.True(File.Exists(spec.Output.SpawnScriptPath));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateFromSpecAsyncModuleLuaDefaultWritesDespiteAcknowledgedModuleBlocker()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "eq2-questparser-module-default-blocker-" + Guid.NewGuid().ToString("N"));
        var spec = BuildWritableWorkflowSpec(tempRoot, QuestGenerationMode.ModuleLua);
        spec.Stages[1].Steps[0].Number = spec.Stages[0].Steps[0].Number;
        Directory.CreateDirectory(Path.GetDirectoryName(spec.Output.PreviewPath)!);
        try
        {
            var diagnostics = QuestSpecValidator.Validate(spec, overwrite: true);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "DUPLICATE_STEP_ID");

            await new QuestWorkflow().GenerateFromSpecAsync(spec, overwrite: true);

            Assert.True(File.Exists(spec.Output.LuaPath));
            Assert.True(File.Exists(spec.Output.SpecPath));
            Assert.True(File.Exists(spec.Output.SqlPath));
            Assert.True(File.Exists(spec.Output.MissingReportPath));
            Assert.True(File.Exists(spec.Output.SpawnScriptPath));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateFromSpecAsyncModuleLuaStrictBlockerThrowsBeforeWriting()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "eq2-questparser-module-strict-blocker-" + Guid.NewGuid().ToString("N"));
        var spec = BuildWritableWorkflowSpec(tempRoot, QuestGenerationMode.ModuleLua);
        spec.Stages[1].Steps[0].Number = spec.Stages[0].Steps[0].Number;
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new QuestWorkflow().GenerateFromSpecAsync(
                    spec,
                    overwrite: true,
                    CancellationToken.None,
                    QuestGenerationMode.ModuleLua,
                    strictModuleLuaValidation: true));

            Assert.Contains("DUPLICATE_STEP_ID", exception.Message);
            Assert.False(File.Exists(spec.Output.PreviewPath));
            Assert.False(File.Exists(spec.Output.LuaPath));
            Assert.False(File.Exists(spec.Output.SpecPath));
            Assert.False(File.Exists(spec.Output.SqlPath));
            Assert.False(File.Exists(spec.Output.MissingReportPath));
            Assert.False(File.Exists(spec.Output.SpawnScriptPath));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
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
    public void SqlGeneratorWritesQuestUpsertForResolvedQuestId()
    {
        var spec = BuildResolvedSpec();
        spec.QuestId = ResolvedReference.Resolved("quest", spec.Quest.Name, 6288, spec.Quest.Name);

        var sql = new SqlReportGenerator().GenerateSql(spec);

        Assert.Contains("INSERT INTO quests", sql);
        Assert.Contains("VALUES (6288,", sql);
        Assert.Contains("ON DUPLICATE KEY UPDATE", sql);
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

    private static QuestSpec BuildAllStepTypesSpec()
    {
        var steps = Enum.GetValues<StepType>().Select((type, index) => new QuestStepSpec
        {
            Number = index + 1,
            Type = type,
            Description = $"{type} step",
            CompletedDescription = $"{type} complete",
            QuantityMax = 1,
            IconId = 1,
            Target = ResolvedReference.Resolved(QuestSpecFactory.KindForStepType(type), $"{type} target", 1000 + index, $"{type} target"),
            Location = type switch
            {
                StepType.Location => new LocationTarget { X = 10.125f, Y = 20.25f, Z = -30.375f, Radius = 15.125f },
                StepType.ZoneLocation => new LocationTarget
                {
                    X = 40.125f,
                    Y = 50.25f,
                    Z = 60.375f,
                    Radius = 25.125f,
                    Zone = ResolvedReference.Resolved("zone", "Commonlands", 12, "Commonlands")
                },
                _ => null
            }
        }).ToList();

        return new QuestSpec
        {
            Quest = new QuestMetadata { Name = "All Step Types", Zone = "Commonlands", CompletionText = "Done." },
            QuestId = ResolvedReference.Resolved("quest", "All Step Types", 9003, "All Step Types"),
            Stages =
            [
                new QuestStageSpec
                {
                    Number = 1,
                    Description = "All step types.",
                    CompletedDescription = "All step types complete.",
                    Steps = steps
                }
            ]
        };
    }

    private static QuestSpec BuildLocationSpec()
    {
        return new QuestSpec
        {
            Quest = new QuestMetadata { Name = "Location Quest", Zone = "Commonlands", CompletionText = "Done." },
            QuestId = ResolvedReference.Resolved("quest", "Location Quest", 9004, "Location Quest"),
            Stages =
            [
                new QuestStageSpec
                {
                    Number = 1,
                    Description = "Visit places.",
                    CompletedDescription = "Visited places.",
                    Steps =
                    [
                        new QuestStepSpec
                        {
                            Number = 1,
                            Type = StepType.Location,
                            Description = "Visit a place",
                            CompletedDescription = "Visited a place.",
                            IconId = 12,
                            Location = new LocationTarget { X = 1.234f, Y = 2, Z = -3.456f, Radius = 12.345f }
                        },
                        new QuestStepSpec
                        {
                            Number = 2,
                            Type = StepType.ZoneLocation,
                            Description = "Visit a zone place",
                            CompletedDescription = "Visited a zone place.",
                            IconId = 13,
                            Location = new LocationTarget
                            {
                                X = 4,
                                Y = 5.5f,
                                Z = 6,
                                Radius = 25,
                                Zone = ResolvedReference.Resolved("zone", "Commonlands", 12, "Commonlands")
                            }
                        }
                    ]
                }
            ]
        };
    }

    private static QuestSpec BuildQuantityRangeModuleSpec()
    {
        return new QuestSpec
        {
            Quest = new QuestMetadata { Name = "Module Range Quest", Zone = "Commonlands", CompletionText = "Done." },
            QuestId = ResolvedReference.Resolved("quest", "Module Range Quest", 9007, "Module Range Quest"),
            Stages =
            [
                new QuestStageSpec
                {
                    Number = 1,
                    Description = "Kill a ranged quantity.",
                    CompletedDescription = "Killed a ranged quantity.",
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
                            Target = ResolvedReference.Resolved("npc", "tier 2 creatures", 330001, "tier 2 creatures")
                        }
                    ]
                }
            ]
        };
    }

    private static QuestSpec BuildRandomModuleSpec()
    {
        return new QuestSpec
        {
            Quest = new QuestMetadata { Name = "Random Module Quest", Zone = "Commonlands", CompletionText = "Done." },
            QuestId = ResolvedReference.Resolved("quest", "Random Module Quest", 9005, "Random Module Quest"),
            Stages =
            [
                new QuestStageSpec
                {
                    Number = 1,
                    Description = "Kill one random target.",
                    CompletedDescription = "Killed one random target.",
                    Steps =
                    [
                        new QuestStepSpec
                        {
                            Number = 1,
                            Type = StepType.Kill,
                            Description = "Kill a random target",
                            CompletedDescription = "Killed a random target.",
                            QuantityMax = 1,
                            IconId = 611,
                            RandomOptions =
                            [
                                new QuestStepOptionSpec
                                {
                                    Description = "Kill stone beetles",
                                    CompletedDescription = "Killed stone beetles.",
                                    QuantityMin = 3,
                                    QuantityMax = 5,
                                    Percentage = 100,
                                    IconId = 611,
                                    Target = ResolvedReference.Resolved("npc", "stone beetles", 330070, "stone beetles")
                                },
                                new QuestStepOptionSpec
                                {
                                    Description = "Kill dervish cutthroats",
                                    CompletedDescription = "Killed dervish cutthroats.",
                                    QuantityMax = 7,
                                    Percentage = 100,
                                    IconId = 611,
                                    Target = ResolvedReference.Resolved("npc", "dervish cutthroats", 330092, "dervish cutthroats")
                                }
                            ]
                        }
                    ]
                }
            ]
        };
    }

    private static QuestSpec BuildTargetlessBasicSpec()
    {
        return new QuestSpec
        {
            Quest = new QuestMetadata { Name = "Basic Step Quest", Zone = "Commonlands", CompletionText = "Done." },
            QuestId = ResolvedReference.Resolved("quest", "Basic Step Quest", 9007, "Basic Step Quest"),
            Stages =
            [
                new QuestStageSpec
                {
                    Number = 1,
                    Description = "Do something.",
                    CompletedDescription = "Did something.",
                    Steps =
                    [
                        new QuestStepSpec
                        {
                            Number = 1,
                            Type = StepType.Generic,
                            Description = "Do something",
                            CompletedDescription = "Did something.",
                            QuantityMax = 1,
                            IconId = 1
                        }
                    ]
                }
            ]
        };
    }

    private static QuestSpec BuildModuleLuaSmokeSpec()
    {
        var spec = BuildRandomModuleSpec();
        spec.Quest.Name = "Module Lua Smoke";
        spec.QuestId = ResolvedReference.Resolved("quest", "Module Lua Smoke", 9006, "Module Lua Smoke");
        spec.Stages[0].Steps.Insert(0, new QuestStepSpec
        {
            Number = 1,
            Type = StepType.Kill,
            Description = "Kill a target",
            CompletedDescription = "Killed a target.",
            QuantityMax = 2,
            IconId = 611,
            Target = ResolvedReference.Resolved("npc", "a target", 330001, "a target")
        });
        spec.Stages[0].Steps.Insert(1, new QuestStepSpec
        {
            Number = 2,
            Type = StepType.ZoneLocation,
            Description = "Visit a zone location",
            CompletedDescription = "Visited a zone location.",
            IconId = 12,
            Location = new LocationTarget
            {
                X = 10,
                Y = 20,
                Z = 30,
                Radius = 15,
                Zone = ResolvedReference.Resolved("zone", "Commonlands", 12, "Commonlands")
            }
        });
        spec.Stages[0].Steps[2].Number = 3;
        spec.Stages[0].Steps.Add(new QuestStepSpec
        {
            Number = 4,
            Type = StepType.Generic,
            Description = "Do a basic task",
            CompletedDescription = "Did a basic task.",
            QuantityMax = 1,
            IconId = 1
        });
        return spec;
    }

    private static string? FindLuaExecutable()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string[] candidates = ["lua.exe", "lua", "lua54.exe", "lua54", "lua53.exe", "lua53", "luajit.exe", "luajit"];
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var cleanDirectory = directory.Trim('"');
            foreach (var candidate in candidates)
            {
                var fullPath = Path.Combine(cleanDirectory, candidate);
                if (File.Exists(fullPath))
                    return fullPath;
            }
        }

        return null;
    }

    private static string QuestModuleSmokeStubLua() => """
        QuestModule = QuestModule or {}
        QuestModule.added = {}

        local targetStepTypes = {
            basic = false,
            chat = true,
            craft = true,
            harvest = true,
            kill = true,
            killByRace = true,
            obtainItem = true,
            spell = true
        }

        local locationStepTypes = {
            location = true,
            zoneLoc = true
        }

        local preservedRandomFields = {
            "id",
            "type",
            "complete",
            "progress",
            "failed",
            "manualComplete"
        }

        local function requireText(step, field)
            assert(type(step[field]) == "string" and step[field] ~= "", "missing " .. field .. " on step " .. tostring(step.id))
        end

        local function validateCount(step)
            local count = step.count
            if type(count) == "number" then
                assert(count > 0, "step " .. tostring(step.id) .. " count must be positive")
                return
            end

            assert(type(count) == "table", "step " .. tostring(step.id) .. " count must be a number or range table")
            assert(type(count.min) == "number" and count.min > 0, "step " .. tostring(step.id) .. " count.min must be positive")
            assert(type(count.max) == "number" and count.max >= count.min, "step " .. tostring(step.id) .. " count.max must be >= count.min")
        end

        local function validateTargets(step, required)
            if step.targets == nil then
                assert(not required, "step " .. tostring(step.id) .. " requires targets")
                return
            end

            assert(type(step.targets) == "table" and #step.targets > 0, "step " .. tostring(step.id) .. " targets must be a non-empty array")
            for i = 1, #step.targets do
                assert(type(step.targets[i]) == "number" and step.targets[i] > 0, "step " .. tostring(step.id) .. " target must be positive")
            end
        end

        local function validateLocations(step)
            assert(step.targets == nil, "location step " .. tostring(step.id) .. " must not emit targets")
            assert(type(step.maxVariation) == "number" and step.maxVariation > 0, "location step " .. tostring(step.id) .. " requires maxVariation")
            assert(type(step.locations) == "table" and #step.locations > 0, "location step " .. tostring(step.id) .. " requires locations")
            for i = 1, #step.locations do
                local location = step.locations[i]
                assert(type(location.x) == "number", "location x required")
                assert(type(location.y) == "number", "location y required")
                assert(type(location.z) == "number", "location z required")
                if step.type == "zoneLoc" then
                    assert(type(location.zone) == "number" and location.zone > 0, "zoneLoc requires zone")
                else
                    assert(location.zone == nil, "location must not emit zone")
                end
            end
        end

        local function mergeRandomOption(step, option)
            local merged = {}
            for key, value in pairs(step) do
                merged[key] = value
            end
            for key, value in pairs(option) do
                merged[key] = value
            end
            for i = 1, #preservedRandomFields do
                local key = preservedRandomFields[i]
                merged[key] = step[key]
            end
            merged.randomOptions = nil
            return merged
        end

        local validateStep

        local function validateRandomOptions(step)
            if step.randomOptions == nil then
                return false
            end

            assert(type(step.randomOptions) == "table" and #step.randomOptions > 0, "step " .. tostring(step.id) .. " randomOptions must be non-empty")
            assert(step.targets == nil, "random parent step " .. tostring(step.id) .. " must not emit targets")
            for i = 1, #step.randomOptions do
                assert(type(step.randomOptions[i]) == "table", "random option must be a table")
                validateStep(mergeRandomOption(step, step.randomOptions[i]))
            end
            return true
        end

        validateStep = function(step)
            assert(type(step.id) == "number" and step.id > 0, "step requires id")
            assert(targetStepTypes[step.type] ~= nil or locationStepTypes[step.type] ~= nil, "unsupported type " .. tostring(step.type))
            validateCount(step)
            requireText(step, "taskGroupText")
            requireText(step, "taskGroupDescription")
            requireText(step, "completeText")
            requireText(step, "completeDescription")
            requireText(step, "completeTaskGroup")
            if step.completeTaskGroupDescription ~= nil then
                requireText(step, "completeTaskGroupDescription")
            end

            if validateRandomOptions(step) then
                return
            end

            if locationStepTypes[step.type] then
                validateLocations(step)
            else
                validateTargets(step, targetStepTypes[step.type])
            end
        end

        function QuestModule.ExportStepHandlers(steps, options)
            local overwrite = type(options) == "table" and options.overwrite == true
            for _, step in ipairs(steps) do
                validateStep(step)
                assert(overwrite or _G[step.complete] == nil, "duplicate step handler " .. tostring(step.complete))
                _G[step.complete] = function(Quest, QuestGiver, Player)
                    if type(step.onComplete) == "function" then
                        step.onComplete(Quest, QuestGiver, Player)
                    end
                end
            end
        end

        function QuestModule.ExportStageStepHandlers(stages, options)
            local allSteps = {}
            for _, steps in ipairs(stages or {}) do
                for _, step in ipairs(steps) do
                    allSteps[#allSteps + 1] = step
                end
            end
            QuestModule.ExportStepHandlers(allSteps, options)
            return allSteps
        end

        function QuestModule.AddSteps(Quest, steps)
            for _, step in ipairs(steps) do
                validateStep(step)
                QuestModule.added[#QuestModule.added + 1] = step
            end
        end

        function QuestModule.ReloadByStep()
            return true
        end
        """;

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static void AssertDiagnostic(
        IEnumerable<QuestDiagnostic> diagnostics,
        QuestDiagnosticSeverity severity,
        string code,
        string messageFragment)
    {
        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Severity == severity
                && diagnostic.Code == code
                && diagnostic.Message.Contains(messageFragment, StringComparison.OrdinalIgnoreCase));
    }

    private static QuestSpec BuildWritableWorkflowSpec(string tempRoot, QuestGenerationMode generationMode)
    {
        var questDirectory = Path.Combine(tempRoot, "content", "Quests", "Commonlands");
        var spawnDirectory = Path.Combine(tempRoot, "content", "SpawnScripts", "Commonlands");
        var previewDirectory = Path.Combine(tempRoot, "preview");
        var baseName = "workflow_blocker";
        return new QuestSpec
        {
            GenerationMode = generationMode,
            Quest = new QuestMetadata
            {
                Name = "Workflow Blocker",
                Zone = "Commonlands",
                CompletionText = "Done."
            },
            QuestId = ResolvedReference.Resolved("quest", "Workflow Blocker", 9009, "Workflow Blocker"),
            Giver = ResolvedReference.Resolved("npc", "Quest Giver", 331133, "Quest Giver"),
            Output = new OutputPaths
            {
                ContentRoot = Path.Combine(tempRoot, "content"),
                QuestDirectory = questDirectory,
                LuaPath = Path.Combine(questDirectory, baseName + ".lua"),
                SpecPath = Path.Combine(questDirectory, baseName + ".quest.json"),
                SqlPath = Path.Combine(questDirectory, baseName + ".quest.sql"),
                MissingReportPath = Path.Combine(questDirectory, baseName + ".missing.md"),
                PreviewPath = Path.Combine(previewDirectory, baseName + ".lua"),
                SpawnScriptPath = Path.Combine(spawnDirectory, "QuestGiver.example.lua")
            },
            Stages =
            [
                new QuestStageSpec
                {
                    Number = 1,
                    Description = "First stage.",
                    CompletedDescription = "First stage done.",
                    Steps =
                    [
                        new QuestStepSpec
                        {
                            Number = 1,
                            Type = StepType.Kill,
                            Description = "Kill a target",
                            CompletedDescription = "Killed a target.",
                            QuantityMax = 1,
                            Target = ResolvedReference.Resolved("npc", "target one", 330001, "target one")
                        }
                    ]
                },
                new QuestStageSpec
                {
                    Number = 2,
                    Description = "Second stage.",
                    CompletedDescription = "Second stage done.",
                    Steps =
                    [
                        new QuestStepSpec
                        {
                            Number = 2,
                            Type = StepType.Kill,
                            Description = "Kill another target",
                            CompletedDescription = "Killed another target.",
                            QuantityMax = 1,
                            Target = ResolvedReference.Resolved("npc", "target two", 330002, "target two")
                        }
                    ]
                }
            ]
        };
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
