using System.Net;
using System.Text.Json;
using QuestParser.Core;

namespace QuestParser.Tests;

public sealed class CliSmokeTests
{
    [Fact]
    public async Task HelpListsModuleLuaMode()
    {
        var originalOut = Console.Out;
        await using var output = new StringWriter();
        Console.SetOut(output);
        try
        {
            var exitCode = await ProgramMain.RunAsync(["help"]);

            Assert.Equal(0, exitCode);
            Assert.Contains("[--mode legacy-spawn-stub|module-lua]", output.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task CreateCommandRunsWithFixtureCensusAndFakeResolver()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "eq2-questparser-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var http = new HttpClient(new FixtureHandler());
            var workflow = new QuestWorkflow(
                censusClient: new CensusClient(http, Path.Combine(tempRoot, "cache")),
                resolver: new Resolver());

            var exitCode = await ProgramMain.RunAsync([
                "create",
                "--quest", "A Hunter's Tool",
                "--author", "Tester",
                "--content-root", tempRoot
            ], workflow);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(Path.Combine(tempRoot, "Quests", "Commonlands", "a_hunters_tool.lua")));
            Assert.True(File.Exists(Path.Combine(tempRoot, "Quests", "Commonlands", "a_hunters_tool.quest.json")));
            Assert.True(File.Exists(Path.Combine(tempRoot, "Quests", "Commonlands", "a_hunters_tool.quest.sql")));
            Assert.True(File.Exists(Path.Combine(tempRoot, "Quests", "Commonlands", "a_hunters_tool.missing.md")));
            Assert.True(File.Exists(Path.Combine(tempRoot, "SpawnScripts", "Commonlands", "JPFeterman.example.lua")));
            var defaultLua = await File.ReadAllTextAsync(Path.Combine(tempRoot, "Quests", "Commonlands", "a_hunters_tool.lua"));
            Assert.DoesNotContain("SpawnScripts/Generic/QuestModule", defaultLua);
            Assert.DoesNotContain("QuestModule.AddSteps", defaultLua);
            await AssertSpecGenerationModeAsync(
                Path.Combine(tempRoot, "Quests", "Commonlands", "a_hunters_tool.quest.json"),
                "LegacySpawnStub");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CreateCommandModuleLuaModeWritesQuestModuleRequire()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "eq2-questparser-cli-mode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var workflow = CreateFixtureWorkflow(tempRoot);

            var exitCode = await ProgramMain.RunAsync([
                "create",
                "--quest", "A Hunter's Tool",
                "--author", "Tester",
                "--content-root", tempRoot,
                "--mode", "module-lua"
            ], workflow);

            Assert.Equal(0, exitCode);
            var lua = await File.ReadAllTextAsync(Path.Combine(tempRoot, "Quests", "Commonlands", "a_hunters_tool.lua"));
            Assert.Contains("require \"SpawnScripts/Generic/QuestModule\"", lua);
            Assert.DoesNotContain("local QuestModule = require", lua);
            Assert.Contains("QuestModule.AddSteps(Quest, STAGE_1_STEPS)", lua);
            await AssertSpecGenerationModeAsync(
                Path.Combine(tempRoot, "Quests", "Commonlands", "a_hunters_tool.quest.json"),
                "ModuleLua");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CreateCommandModuleLuaModePrintsMissingQuestModuleWarning()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "eq2-questparser-cli-module-warning-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var originalOut = Console.Out;
        await using var output = new StringWriter();
        Console.SetOut(output);
        try
        {
            var workflow = CreateFixtureWorkflow(tempRoot);

            var exitCode = await ProgramMain.RunAsync([
                "create",
                "--quest", "A Hunter's Tool",
                "--author", "Tester",
                "--content-root", tempRoot,
                "--mode", "module-lua"
            ], workflow);

            Assert.Equal(0, exitCode);
            var text = output.ToString();
            Assert.Contains("MODULE_LUA_MISSING_QUEST_MODULE", text);
            Assert.Contains("SpawnScripts/Generic/QuestModule.lua", text);
        }
        finally
        {
            Console.SetOut(originalOut);
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CreateCommandModuleLuaBlockerReturnsNonZeroAndDoesNotWriteOutputs()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "eq2-questparser-cli-create-module-blocker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var originalError = Console.Error;
        await using var error = new StringWriter();
        Console.SetError(error);
        try
        {
            var workflow = CreateFixtureWorkflow(tempRoot, new DuplicateStepResolver());

            var exitCode = await ProgramMain.RunAsync([
                "create",
                "--quest", "A Hunter's Tool",
                "--author", "Tester",
                "--content-root", tempRoot,
                "--mode", "module-lua"
            ], workflow);

            Assert.Equal(1, exitCode);
            Assert.Contains("DUPLICATE_STEP_ID", error.ToString());
            Assert.False(File.Exists(Path.Combine(tempRoot, "Quests", "Commonlands", "a_hunters_tool.lua")));
            Assert.False(File.Exists(Path.Combine(tempRoot, "Quests", "Commonlands", "a_hunters_tool.quest.json")));
            Assert.False(File.Exists(Path.Combine(tempRoot, "Quests", "Commonlands", "a_hunters_tool.quest.sql")));
            Assert.False(File.Exists(Path.Combine(tempRoot, "Quests", "Commonlands", "a_hunters_tool.missing.md")));
            Assert.False(File.Exists(Path.Combine(tempRoot, "SpawnScripts", "Commonlands", "JPFeterman.example.lua")));
        }
        finally
        {
            Console.SetError(originalError);
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateCommandModuleLuaModePersistsSpecMode()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "eq2-questparser-cli-generate-mode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var workflow = CreateFixtureWorkflow(tempRoot);
            var createResult = await workflow.CreateAsync("A Hunter's Tool", tempRoot, "Tester");

            var exitCode = await ProgramMain.RunAsync([
                "generate",
                "--spec", createResult.Spec.Output.SpecPath,
                "--mode", "module-lua",
                "--overwrite"
            ], workflow);

            Assert.Equal(0, exitCode);
            await AssertSpecGenerationModeAsync(createResult.Spec.Output.SpecPath, "ModuleLua");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateCommandModuleLuaModePrintsMissingQuestModuleWarning()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "eq2-questparser-cli-generate-warning-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var originalOut = Console.Out;
        await using var output = new StringWriter();
        Console.SetOut(output);
        try
        {
            var workflow = CreateFixtureWorkflow(tempRoot);
            var createResult = await workflow.CreateAsync("A Hunter's Tool", tempRoot, "Tester");

            var exitCode = await ProgramMain.RunAsync([
                "generate",
                "--spec", createResult.Spec.Output.SpecPath,
                "--mode", "module-lua",
                "--overwrite"
            ], workflow);

            Assert.Equal(0, exitCode);
            var text = output.ToString();
            Assert.Contains("MODULE_LUA_MISSING_QUEST_MODULE", text);
            Assert.Contains("SpawnScripts/Generic/QuestModule.lua", text);
        }
        finally
        {
            Console.SetOut(originalOut);
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateCommandModuleLuaBlockerReturnsNonZeroAndDoesNotWriteOutputs()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "eq2-questparser-cli-module-blocker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var originalError = Console.Error;
        await using var error = new StringWriter();
        Console.SetError(error);
        try
        {
            var spec = BuildGenerateSpec(tempRoot);
            spec.Stages[1].Steps[0].Number = spec.Stages[0].Steps[0].Number;
            Directory.CreateDirectory(Path.GetDirectoryName(spec.Output.PreviewPath)!);
            await QuestWorkflow.WriteSpecAsync(spec);

            var exitCode = await ProgramMain.RunAsync([
                "generate",
                "--spec", spec.Output.SpecPath,
                "--mode", "module-lua",
                "--overwrite"
            ], new QuestWorkflow());

            Assert.Equal(1, exitCode);
            Assert.Contains("DUPLICATE_STEP_ID", error.ToString());
            Assert.False(File.Exists(spec.Output.PreviewPath));
            Assert.False(File.Exists(spec.Output.LuaPath));
            Assert.False(File.Exists(spec.Output.SqlPath));
            Assert.False(File.Exists(spec.Output.MissingReportPath));
            Assert.False(File.Exists(spec.Output.SpawnScriptPath));
        }
        finally
        {
            Console.SetError(originalError);
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task WorkflowOldPositionalCancellationTokenCallsKeepLegacyMode()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "eq2-questparser-workflow-compat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var workflow = CreateFixtureWorkflow(tempRoot);

            var createResult = await workflow.CreateAsync("A Hunter's Tool", tempRoot, "Tester", false, CancellationToken.None);
            Assert.Equal(QuestGenerationMode.LegacySpawnStub, createResult.Spec.GenerationMode);

            var generateResult = await workflow.GenerateAsync(createResult.Spec.Output.SpecPath, true, CancellationToken.None);
            Assert.Equal(QuestGenerationMode.LegacySpawnStub, generateResult.Spec.GenerationMode);

            var fromSpecResult = await workflow.GenerateFromSpecAsync(generateResult.Spec, true, CancellationToken.None);
            Assert.Equal(QuestGenerationMode.LegacySpawnStub, fromSpecResult.Spec.GenerationMode);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CreateCommandRejectsInvalidMode()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "eq2-questparser-cli-invalid-mode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var workflow = CreateFixtureWorkflow(tempRoot);

            var exitCode = await ProgramMain.RunAsync([
                "create",
                "--quest", "A Hunter's Tool",
                "--content-root", tempRoot,
                "--mode", "experimental"
            ], workflow);

            Assert.Equal(1, exitCode);
            Assert.False(File.Exists(Path.Combine(tempRoot, "Quests", "Commonlands", "a_hunters_tool.quest.json")));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CreateCommandRejectsMissingModeValue()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "eq2-questparser-cli-missing-mode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var workflow = CreateFixtureWorkflow(tempRoot);

            var exitCode = await ProgramMain.RunAsync([
                "create",
                "--quest", "A Hunter's Tool",
                "--content-root", tempRoot,
                "--mode"
            ], workflow);

            Assert.Equal(1, exitCode);
            Assert.False(File.Exists(Path.Combine(tempRoot, "Quests", "Commonlands", "a_hunters_tool.quest.json")));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ImportCommandCanUseLocalCensusJsonDirectory()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "eq2-questparser-cli-local-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(tempRoot, "source");
        var cache = Path.Combine(tempRoot, "cache");
        var contentRoot = Path.Combine(tempRoot, "content");
        Directory.CreateDirectory(source);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(source, CensusClient.QuestJsonFileName("A Hunter's Tool")), QuestParserCoreTests.SampleQuestJson());
            await File.WriteAllTextAsync(Path.Combine(source, CensusClient.QuestGiverJsonFileName("A Hunter's Tool")), QuestParserCoreTests.SampleQuestGiverJson());

            var exitCode = await ProgramMain.RunAsync([
                "import",
                "--quest", "A Hunter's Tool",
                "--author", "Tester",
                "--content-root", contentRoot,
                "--census-source", "local",
                "--census-local-dir", source,
                "--census-cache-dir", cache
            ]);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(Path.Combine(contentRoot, "Quests", "Commonlands", "a_hunters_tool.quest.json")));
            Assert.True(File.Exists(Path.Combine(cache, CensusClient.QuestJsonFileName("A Hunter's Tool"))));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static QuestWorkflow CreateFixtureWorkflow(string tempRoot, IQuestDatabaseResolver? resolver = null)
    {
        var http = new HttpClient(new FixtureHandler());
        return new QuestWorkflow(
            censusClient: new CensusClient(http, Path.Combine(tempRoot, "cache")),
            resolver: resolver ?? new Resolver());
    }

    private static async Task AssertSpecGenerationModeAsync(string specPath, string expected)
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(specPath));
        Assert.True(document.RootElement.TryGetProperty("generationMode", out var generationMode));
        Assert.Equal(expected, generationMode.GetString());
    }

    private static QuestSpec BuildGenerateSpec(string tempRoot)
    {
        var questDirectory = Path.Combine(tempRoot, "content", "Quests", "Commonlands");
        var spawnDirectory = Path.Combine(tempRoot, "content", "SpawnScripts", "Commonlands");
        var baseName = "module_blocker";
        return new QuestSpec
        {
            Quest = new QuestMetadata
            {
                Name = "Module Blocker",
                Zone = "Commonlands",
                CompletionText = "Done."
            },
            QuestId = ResolvedReference.Resolved("quest", "Module Blocker", 9008, "Module Blocker"),
            Giver = ResolvedReference.Resolved("npc", "Quest Giver", 331133, "Quest Giver"),
            Output = new OutputPaths
            {
                ContentRoot = Path.Combine(tempRoot, "content"),
                QuestDirectory = questDirectory,
                LuaPath = Path.Combine(questDirectory, baseName + ".lua"),
                SpecPath = Path.Combine(questDirectory, baseName + ".quest.json"),
                SqlPath = Path.Combine(questDirectory, baseName + ".quest.sql"),
                MissingReportPath = Path.Combine(questDirectory, baseName + ".missing.md"),
                PreviewPath = Path.Combine(tempRoot, "preview", baseName + ".lua"),
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

    private sealed class FixtureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.RequestUri?.AbsolutePath.Contains("questgiver", StringComparison.OrdinalIgnoreCase) == true
                ? QuestParserCoreTests.SampleQuestGiverJson()
                : QuestParserCoreTests.SampleQuestJson();

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
        }
    }

    private sealed class Resolver : IQuestDatabaseResolver
    {
        public Task ResolveAsync(QuestSpec spec, CancellationToken cancellationToken = default)
        {
            spec.QuestId = ResolvedReference.Proposed("quest", spec.Quest.Name, 9001, spec.Quest.Name);
            spec.Giver = ResolvedReference.Resolved("npc", "J.P. Feterman", 331133, "J.P. Feterman");
            foreach (var step in spec.Stages.SelectMany(stage => stage.Steps))
                step.Target = ResolvedReference.Resolved(QuestSpecFactory.KindForStepType(step.Type), step.SearchText, 1000 + step.Number, step.SearchText);
            return Task.CompletedTask;
        }
    }

    private sealed class DuplicateStepResolver : IQuestDatabaseResolver
    {
        public Task ResolveAsync(QuestSpec spec, CancellationToken cancellationToken = default)
        {
            spec.QuestId = ResolvedReference.Proposed("quest", spec.Quest.Name, 9001, spec.Quest.Name);
            spec.Giver = ResolvedReference.Resolved("npc", "J.P. Feterman", 331133, "J.P. Feterman");
            foreach (var step in spec.Stages.SelectMany(stage => stage.Steps))
                step.Target = ResolvedReference.Resolved(QuestSpecFactory.KindForStepType(step.Type), step.SearchText, 1000 + step.Number, step.SearchText);

            spec.Stages[1].Steps[0].Number = spec.Stages[0].Steps[0].Number;
            return Task.CompletedTask;
        }
    }
}
