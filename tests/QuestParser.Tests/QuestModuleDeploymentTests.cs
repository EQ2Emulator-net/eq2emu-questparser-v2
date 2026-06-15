using QuestParser.Core;

namespace QuestParser.Tests;

public sealed class QuestModuleDeploymentTests
{
    [Fact]
    public void UsesQuestsGenericAsCanonicalTarget()
    {
        Assert.Equal("Quests/Generic/QuestModule.lua", QuestModuleDeployment.TargetRelativePath);
        Assert.Equal("Quests/Generic/QuestModule", QuestModuleDeployment.RequirePath);
    }

    [Fact]
    public void ReportsMissingWhenModuleIsAbsent()
    {
        var contentRoot = NewTempRoot();
        try
        {
            var status = QuestModuleDeployment.GetStatus(contentRoot);

            Assert.Equal(QuestModuleDeploymentState.Missing, status.State);
            Assert.Equal(Path.Combine(contentRoot, "Quests", "Generic", "QuestModule.lua"), status.TargetPath);
            Assert.Null(status.ActualHash);
            Assert.False(string.IsNullOrWhiteSpace(status.ExpectedHash));
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public void CopyWritesCanonicalModuleAndStatusBecomesCurrent()
    {
        var contentRoot = NewTempRoot();
        try
        {
            QuestModuleDeployment.CopyToContentRoot(contentRoot, overwrite: false);

            var status = QuestModuleDeployment.GetStatus(contentRoot);
            Assert.Equal(QuestModuleDeploymentState.Current, status.State);
            Assert.Equal(status.ExpectedHash, status.ActualHash);
            var text = File.ReadAllText(status.TargetPath);
            Assert.Contains("function QuestModule.ExportStageStepHandlers", text, StringComparison.Ordinal);
            Assert.Contains("function QuestModule.AllComplete", text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public void ReportsOutdatedWhenExistingModuleHashDiffers()
    {
        var contentRoot = NewTempRoot();
        try
        {
            var targetPath = QuestModuleDeployment.TargetPath(contentRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllText(targetPath, "QuestModule = {}\n");

            var status = QuestModuleDeployment.GetStatus(contentRoot);

            Assert.Equal(QuestModuleDeploymentState.Outdated, status.State);
            Assert.NotEqual(status.ExpectedHash, status.ActualHash);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    private static string NewTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "eq2-questmodule-deploy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
