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
            workflow ??= new QuestWorkflow();

            switch (command)
            {
                case "create":
                {
                    var quest = Require(options, "quest");
                    var author = Get(options, "author", "");
                    var contentRoot = Get(options, "content-root", Defaults.ContentRoot);
                    var overwrite = Has(options, "overwrite");
                    var result = await workflow.CreateAsync(quest, contentRoot, author, overwrite);
                    PrintResult("Created quest assets", result);
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
                    var result = await workflow.GenerateAsync(spec, overwrite);
                    PrintResult("Generated quest assets", result);
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

    private static void PrintResult(string title, QuestWorkflowResult result)
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
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            EQ2Emu QuestParser

            Commands:
              questparser create --quest "<quest name>" [--author "<name>"] [--overwrite]
              questparser import --quest "<quest name>" [--author "<name>"]
              questparser resolve --spec "<path-to-json>"
              questparser generate --spec "<path-to-json>" [--overwrite]
              questparser lint [--content-root "<path>"]

            Defaults:
              Census service id: s:example, or EQ2QP_CENSUS_SERVICE_ID
              DB: optional; set EQ2QP_DB_CONNECTION or EQ2QP_DB_HOST/EQ2QP_DB_NAME/EQ2QP_DB_USER/EQ2QP_DB_PASSWORD
              Content root: ./eq2emu-content, or EQ2QP_CONTENT_ROOT
            """);
    }
}
