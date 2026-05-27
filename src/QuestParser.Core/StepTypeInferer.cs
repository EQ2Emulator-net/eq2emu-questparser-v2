using System.Text.RegularExpressions;

namespace QuestParser.Core;

public static partial class StepTypeInferer
{
    public static StepType Infer(string description, string completedText, string iconName)
    {
        var text = $"{description} {completedText} {iconName}".ToLowerInvariant();

        if (Contains(text, @"\b(speak|talk|return|meet|find|visit|hail)\b"))
            return StepType.Chat;
        if (Contains(text, @"\b(kill|slay|defeat|hunt|destroy|eliminate|cutthroats?|bandits?|orcs?|gnolls?|dervishes?|skeletons?|zombies?|rats?|goblins?|bears?|wolves?|spiders?|snakes?|giants?)\b"))
            return StepType.Kill;
        if (Contains(text, @"\b(harvest|gather|pick up|collect|roots?|ore|wood|stone|bush|tree|fungus)\b"))
            return StepType.Harvest;
        if (Contains(text, @"\b(craft|make|create|forge|sew|scribe)\b"))
            return StepType.Craft;
        if (Contains(text, @"\b(cast|spell|blessing|incantation)\b"))
            return StepType.Spell;
        if (Contains(text, @"\b(travel|go to|search|check|investigate|ride|walk|location)\b"))
            return StepType.Location;
        if (Contains(text, @"\b(obtain|recover|retrieve|buy|purchase|get|deliver)\b"))
            return StepType.ObtainItem;

        return StepType.Generic;
    }

    public static string InferSearchText(StepType type, string description, string iconName, string giverName)
    {
        if (type == StepType.Chat && LooksLikeReturnStep(description) && !string.IsNullOrWhiteSpace(giverName))
            return giverName;

        if ((type is StepType.Harvest or StepType.ObtainItem or StepType.Craft or StepType.Spell) && !string.IsNullOrWhiteSpace(iconName))
            return iconName;

        var text = description.Trim();
        var lower = text.ToLowerInvariant();
        var markers = type switch
        {
            StepType.Kill or StepType.KillByRace => new[] { "from ", "kill ", "slay ", "defeat ", "hunt " },
            StepType.Chat => new[] { "speak with ", "speak to ", "talk to ", "return to ", "meet " },
            StepType.Harvest or StepType.ObtainItem => new[] { "from ", "gather ", "collect ", "retrieve ", "recover ", "obtain ", "buy " },
            StepType.Craft => new[] { "craft ", "make ", "create " },
            _ => Array.Empty<string>()
        };

        foreach (var marker in markers)
        {
            var index = lower.IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0)
            {
                var candidate = text[(index + marker.Length)..];
                candidate = StopPhraseRegex().Replace(candidate, "").Trim(' ', '.', ',');
                if (!string.IsNullOrWhiteSpace(candidate))
                    return candidate;
            }
        }

        return text.Length > 80 ? text[..80] : text;
    }

    public static bool LooksLikeReturnStep(string description)
    {
        return Contains(description.ToLowerInvariant(), @"\b(return|bring|back|speak with|speak to|talk to)\b");
    }

    private static bool Contains(string text, string pattern) => Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [GeneratedRegex(@"\b(in|near|around|at|for|to|until|and|that|who|which|with)\b.*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StopPhraseRegex();
}
