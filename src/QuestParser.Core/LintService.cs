namespace QuestParser.Core;

public sealed class LintService
{
    public LintResult Lint(string? contentRoot = null)
    {
        var result = new LintResult();
        var questRoot = Path.Combine(contentRoot ?? Defaults.ContentRoot, "Quests");
        if (!Directory.Exists(questRoot))
        {
            result.Findings.Add($"Quest root not found: {questRoot}");
            return result;
        }

        foreach (var file in Directory.EnumerateFiles(questRoot, "*.lua", SearchOption.AllDirectories))
        {
            result.LuaFiles++;
            var text = File.ReadAllText(file);
            if (text.Contains("TODO DB:", StringComparison.OrdinalIgnoreCase))
            {
                result.TodoDbCount++;
                result.Findings.Add($"TODO DB: {file}");
            }
            if (text.Contains("--[[ ID's --]]", StringComparison.OrdinalIgnoreCase))
            {
                result.PlaceholderIdCount++;
                result.Findings.Add($"Placeholder IDs: {file}");
            }
            if (text.Contains("QuestParser (Replace this)", StringComparison.OrdinalIgnoreCase))
            {
                result.LegacyAuthorPlaceholderCount++;
                result.Findings.Add($"Legacy author placeholder: {file}");
            }
        }

        return result;
    }
}
