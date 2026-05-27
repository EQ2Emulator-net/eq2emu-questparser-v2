using System.Net;
using QuestParser.Core;

namespace QuestParser.Tests;

public sealed class CliSmokeTests
{
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
}
