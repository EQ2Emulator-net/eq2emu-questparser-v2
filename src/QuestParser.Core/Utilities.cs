using System.Text;
using System.Text.RegularExpressions;

namespace QuestParser.Core;

public static partial class Utilities
{
    public static string NormalizeQuestFileName(string questName)
    {
        var file = questName.Replace(' ', '_') + ".lua";
        file = InvalidFileCharsRegex().Replace(file, "");
        return file.ToLowerInvariant();
    }

    public static string NormalizeSpecFileName(string questName)
    {
        return Path.ChangeExtension(NormalizeQuestFileName(questName), ".quest.json");
    }

    public static string NormalizeSqlFileName(string questName)
    {
        return Path.ChangeExtension(NormalizeQuestFileName(questName), ".quest.sql");
    }

    public static string NormalizeMissingReportFileName(string questName)
    {
        return Path.ChangeExtension(NormalizeQuestFileName(questName), ".missing.md");
    }

    public static string NormalizeSpawnScriptFileName(string spawnName)
    {
        var identifier = IdentifierFromName(spawnName);
        return (identifier == "QuestId" ? "QuestGiver" : identifier) + ".lua";
    }

    public static string NormalizeSpawnScriptExampleFileName(string spawnName)
    {
        return Path.ChangeExtension(NormalizeSpawnScriptFileName(spawnName), ".example.lua");
    }

    public static string SafeDirectoryName(string value)
    {
        var cleaned = InvalidDirectoryCharsRegex().Replace(value.Replace(" ", ""), "");
        return string.IsNullOrWhiteSpace(cleaned) ? "Unknown" : cleaned;
    }

    public static string LuaString(string value)
    {
        var escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }

    public static string SqlString(string value)
    {
        return "'" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    public static string IdentifierFromName(string name)
    {
        name = name.Replace("'", "", StringComparison.Ordinal);
        var words = WordRegex().Matches(name)
            .Select(m => m.Value)
            .Where(w => w.Length > 0)
            .ToArray();
        if (words.Length == 0)
            return "QuestId";

        var builder = new StringBuilder();
        foreach (var word in words)
            builder.Append(char.ToUpperInvariant(word[0])).Append(word[1..]);
        return builder.ToString();
    }

    public static string CacheKey(string value)
    {
        var normalized = InvalidFileCharsRegex().Replace(value.ToLowerInvariant().Replace(' ', '_'), "");
        return string.IsNullOrWhiteSpace(normalized) ? "quest" : normalized;
    }

    public static string RuntimePath(params string[] parts)
    {
        return Path.Combine([AppContext.BaseDirectory, .. parts]);
    }

    public static (int Copper, int Silver, int Gold, int Platinum) SplitCoin(int totalCopper)
    {
        var platinum = totalCopper / 1_000_000;
        totalCopper %= 1_000_000;
        var gold = totalCopper / 10_000;
        totalCopper %= 10_000;
        var silver = totalCopper / 100;
        var copper = totalCopper % 100;
        return (copper, silver, gold, platinum);
    }

    [GeneratedRegex(@"[^\w\.@-]", RegexOptions.Compiled)]
    private static partial Regex InvalidFileCharsRegex();

    [GeneratedRegex(@"[^\w -]", RegexOptions.Compiled)]
    private static partial Regex InvalidDirectoryCharsRegex();

    [GeneratedRegex(@"[A-Za-z0-9]+", RegexOptions.Compiled)]
    private static partial Regex WordRegex();
}
