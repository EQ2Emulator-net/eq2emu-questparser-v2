using QuestParser.Core;

return await ProgramMain.RunAsync(args);

public static class ProgramMain
{
    public static async Task<int> RunAsync(string[] args, QuestWorkflow? workflow = null, LintService? lintService = null)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        try
        {
            var command = args[0].ToLowerInvariant();
            var options = ParseOptions(args.Skip(1).ToArray());
            workflow ??= CreateWorkflow(options);

            switch (command)
            {
                case "create":
                {
                    var quest = Require(options, "quest");
                    var author = Get(options, "author", "");
                    var contentRoot = Get(options, "content-root", Defaults.ContentRoot);
                    var overwrite = Has(options, "overwrite");
                    var mode = GetGenerationMode(options);
                    var result = await workflow.CreateAsync(quest, contentRoot, author, overwrite, CancellationToken.None, mode, strictModuleLuaValidation: true);
                    PrintResult("Created quest assets", result, includeDiagnostics: true);
                    return 0;
                }
                case "import":
                {
                    var quest = Require(options, "quest");
                    var author = Get(options, "author", "");
                    var contentRoot = Get(options, "content-root", Defaults.ContentRoot);
                    var result = await workflow.ImportAsync(quest, contentRoot, author);
                    PrintResult("Imported quest spec", result);
                    return 0;
                }
                case "resolve":
                {
                    var spec = Require(options, "spec");
                    var result = await workflow.ResolveAsync(spec);
                    PrintResult("Resolved quest spec", result);
                    return 0;
                }
                case "generate":
                {
                    var spec = Require(options, "spec");
                    var overwrite = Has(options, "overwrite");
                    var mode = GetGenerationMode(options);
                    var result = await workflow.GenerateAsync(spec, overwrite, CancellationToken.None, mode, strictModuleLuaValidation: true);
                    PrintResult("Generated quest assets", result, includeDiagnostics: true);
                    return 0;
                }
                case "lint":
                {
                    var contentRoot = Get(options, "content-root", Defaults.ContentRoot);
                    var result = (lintService ?? new LintService()).Lint(contentRoot);
                    Console.WriteLine($"Lua files: {result.LuaFiles}");
                    Console.WriteLine($"TODO DB files: {result.TodoDbCount}");
                    Console.WriteLine($"Placeholder ID files: {result.PlaceholderIdCount}");
                    Console.WriteLine($"Legacy author placeholders: {result.LegacyAuthorPlaceholderCount}");
                    foreach (var finding in result.Findings.Take(200))
                        Console.WriteLine(finding);
                    if (result.Findings.Count > 200)
                        Console.WriteLine($"... {result.Findings.Count - 200} more findings omitted.");
                    return result.TodoDbCount + result.PlaceholderIdCount > 0 ? 2 : 0;
                }
                default:
                    Console.Error.WriteLine($"Unknown command: {args[0]}");
                    PrintHelp();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static Dictionary<string, string?> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
                continue;

            var name = arg[2..];
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                options[name] = args[++i];
            else
                options[name] = null;
        }

        return options;
    }

    private static string Require(Dictionary<string, string?> options, string name)
    {
        var value = Get(options, name, "");
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Missing required option --{name}.");
        return value;
    }

    private static string Get(Dictionary<string, string?> options, string name, string fallback)
    {
        return options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
    }

    private static bool Has(Dictionary<string, string?> options, string name)
    {
        return options.ContainsKey(name);
    }

    private static QuestGenerationMode? GetGenerationMode(Dictionary<string, string?> options)
    {
        if (!options.TryGetValue("mode", out var value))
            return null;

        return value?.ToLowerInvariant() switch
        {
            "legacy-spawn-stub" => QuestGenerationMode.LegacySpawnStub,
            "module-lua" => QuestGenerationMode.ModuleLua,
            null or "" => throw new ArgumentException("Missing value for --mode. Expected legacy-spawn-stub or module-lua."),
            _ => throw new ArgumentException($"Invalid value for --mode: {value}. Expected legacy-spawn-stub or module-lua.")
        };
    }

    private static QuestWorkflow CreateWorkflow(Dictionary<string, string?> options)
    {
        var censusOptions = CensusSourceOptions.FromEnvironment().WithOverrides(
            source: Get(options, "census-source", ""),
            baseUrl: Get(options, "census-base-url", Get(options, "census-remote-base-url", "")),
            serviceId: Get(options, "census-service-id", ""),
            includeServiceId: GetNullableBool(options, "census-include-service-id"),
            localDirectory: Get(options, "census-local-dir", ""),
            cacheDirectory: Get(options, "census-cache-dir", ""));

        return new QuestWorkflow(censusClient: CensusClientFactory.Create(censusOptions));
    }

    private static bool? GetNullableBool(Dictionary<string, string?> options, string name)
    {
        if (!options.TryGetValue(name, out var value))
            return null;
        if (value is null)
            return true;
        if (value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase))
            return true;
        if (value.Equals("0", StringComparison.OrdinalIgnoreCase)
            || value.Equals("false", StringComparison.OrdinalIgnoreCase)
            || value.Equals("no", StringComparison.OrdinalIgnoreCase)
            || value.Equals("off", StringComparison.OrdinalIgnoreCase))
            return false;
        throw new ArgumentException($"Invalid boolean value for --{name}: {value}");
    }

    private static void PrintResult(string title, QuestWorkflowResult result, bool includeDiagnostics = false)
    {
        Console.WriteLine(title);
        Console.WriteLine($"Quest: {result.Spec.Quest.Name}");
        Console.WriteLine($"Quest ID: {result.Spec.QuestId.Status} {result.Spec.QuestId.Id}");
        Console.WriteLine($"Giver: {result.Spec.Giver.Status} {result.Spec.Giver.Name} {result.Spec.Giver.Id}");
        Console.WriteLine("Written files:");
        foreach (var file in result.WrittenFiles)
            Console.WriteLine($"  {file}");
        if (result.Spec.Todos.Count > 0)
        {
            Console.WriteLine("TODOs:");
            foreach (var todo in result.Spec.Todos)
                Console.WriteLine($"  {todo}");
        }
        if (includeDiagnostics)
            PrintDiagnostics(result.Spec);
    }

    private static void PrintDiagnostics(QuestSpec spec)
    {
        var diagnostics = QuestSpecValidator.Validate(spec, overwrite: true);
        if (diagnostics.Count == 0)
            return;

        Console.WriteLine("Diagnostics:");
        foreach (var diagnostic in diagnostics)
            Console.WriteLine($"  {diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            EQ2Emu QuestParser

            Commands:
              questparser create --quest "<quest name>" [--author "<name>"] [--overwrite] [--mode legacy-spawn-stub|module-lua]
              questparser import --quest "<quest name>" [--author "<name>"]
              questparser resolve --spec "<path-to-json>"
              questparser generate --spec "<path-to-json>" [--overwrite] [--mode legacy-spawn-stub|module-lua]
              questparser lint [--content-root "<path>"]

            Defaults:
              Census source: daybreak, remote, or local; set EQ2QP_CENSUS_SOURCE or --census-source
              Census service id: s:example, or EQ2QP_CENSUS_SERVICE_ID / --census-service-id
              Remote Census base URL: EQ2QP_CENSUS_REMOTE_BASE_URL or --census-base-url
              Local Census JSON dir: EQ2QP_CENSUS_LOCAL_DIR or --census-local-dir
              DB: optional; set EQ2QP_DB_CONNECTION or EQ2QP_DB_HOST/EQ2QP_DB_NAME/EQ2QP_DB_USER/EQ2QP_DB_PASSWORD
              Content root: ./eq2emu-content, or EQ2QP_CONTENT_ROOT
            """);
    }
}
