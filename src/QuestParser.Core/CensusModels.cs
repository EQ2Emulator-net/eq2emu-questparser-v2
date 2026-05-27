using System.Text.Json.Serialization;

namespace QuestParser.Core;

public sealed class CensusQuestResponse
{
    [JsonPropertyName("quest_list")]
    public List<CensusQuest> QuestList { get; set; } = [];

    [JsonPropertyName("returned")]
    public int Returned { get; set; }
}

public sealed class CensusQuestGiverResponse
{
    [JsonPropertyName("questgiver_list")]
    public List<CensusQuestGiver> QuestGiverList { get; set; } = [];

    [JsonPropertyName("returned")]
    public int Returned { get; set; }
}

public sealed class CensusQuest
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("level")]
    public byte Level { get; set; }

    [JsonPropertyName("scales_with_level")]
    public int ScalesWithLevel { get; set; }

    [JsonPropertyName("is_tradeskill")]
    public int IsTradeskill { get; set; }

    [JsonPropertyName("crc")]
    public long Crc { get; set; }

    [JsonPropertyName("completion_text")]
    public string CompletionText { get; set; } = "";

    [JsonPropertyName("shareable")]
    public int Shareable { get; set; }

    [JsonPropertyName("starter_text")]
    public string StarterText { get; set; } = "";

    [JsonPropertyName("complete_shareable")]
    public int CompleteShareable { get; set; }

    [JsonPropertyName("tier")]
    public byte Tier { get; set; }

    [JsonPropertyName("repeatable")]
    public int Repeatable { get; set; }

    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("stage_list")]
    public List<CensusStage> StageList { get; set; } = [];

    [JsonPropertyName("reward_list")]
    public List<CensusReward> RewardList { get; set; } = [];
}

public sealed class CensusStage
{
    [JsonPropertyName("num")]
    public int Number { get; set; }

    [JsonPropertyName("starter_text_list")]
    public List<string> StarterTextList { get; set; } = [];

    [JsonPropertyName("completion_text_list")]
    public List<string> CompletionTextList { get; set; } = [];

    [JsonPropertyName("branch_list")]
    public List<CensusBranch> BranchList { get; set; } = [];
}

public sealed class CensusBranch
{
    [JsonPropertyName("quota_min")]
    public int QuantityMin { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("completion_zone_override")]
    public string CompletionZoneOverride { get; set; } = "";

    [JsonPropertyName("quota_max")]
    public int QuantityMax { get; set; }

    [JsonPropertyName("completion_zone")]
    public string CompletionZone { get; set; } = "";

    [JsonPropertyName("completed_text")]
    public string CompletedText { get; set; } = "";

    [JsonPropertyName("icon_name")]
    public string IconName { get; set; } = "";

    [JsonPropertyName("icon_id")]
    public int IconId { get; set; }
}

public sealed class CensusReward
{
    [JsonPropertyName("coin_min")]
    public int CoinMin { get; set; }

    [JsonPropertyName("coin_max")]
    public int CoinMax { get; set; }

    [JsonPropertyName("exp")]
    public double Experience { get; set; }

    [JsonPropertyName("item_list")]
    public List<CensusRewardItem> ItemList { get; set; } = [];

    [JsonPropertyName("factionchange_list")]
    public List<CensusFactionChange> FactionChangeList { get; set; } = [];
}

public sealed class CensusRewardItem
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; } = 1;
}

public sealed class CensusFactionChange
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("amount")]
    public int Amount { get; set; }
}

public sealed class CensusQuestGiver
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("zone")]
    public string Zone { get; set; } = "";

    [JsonPropertyName("quest_list")]
    public List<CensusQuestReference> QuestList { get; set; } = [];
}

public sealed class CensusQuestReference
{
    [JsonPropertyName("id")]
    public long Id { get; set; }
}
