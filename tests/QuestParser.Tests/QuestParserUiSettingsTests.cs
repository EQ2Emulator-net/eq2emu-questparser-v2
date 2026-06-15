using QuestParser.Desktop;

namespace QuestParser.Tests;

public sealed class QuestParserUiSettingsTests
{
    [Fact]
    public void DefaultSettingsHideSettingsSummaryStrip()
    {
        Assert.False(new QuestParserUiSettings().ShowSettingsSummary);
        Assert.False(QuestParserUiSettings.FromEnvironment().ShowSettingsSummary);
    }
}
