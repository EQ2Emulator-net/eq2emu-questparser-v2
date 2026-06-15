using System.Reflection;
using System.Security.Cryptography;

namespace QuestParser.Core;

public enum QuestModuleDeploymentState
{
    Missing,
    Current,
    Outdated
}

public sealed record QuestModuleDeploymentStatus(
    QuestModuleDeploymentState State,
    string TargetPath,
    string ExpectedHash,
    string? ActualHash);

public static class QuestModuleDeployment
{
    public const string TargetRelativePath = "Quests/Generic/QuestModule.lua";
    public const string RequirePath = "Quests/Generic/QuestModule";

    private const string ResourceName = "QuestParser.Core.Assets.QuestModule.lua";

    private static readonly Lazy<byte[]> TemplateBytes = new(ReadTemplateBytes);
    private static readonly Lazy<string> TemplateHash = new(() => HashBytes(TemplateBytes.Value));

    public static string ExpectedHash => TemplateHash.Value;

    public static string TargetPath(string contentRoot)
    {
        return Path.Combine(contentRoot, "Quests", "Generic", "QuestModule.lua");
    }

    public static QuestModuleDeploymentStatus GetStatus(string contentRoot)
    {
        var targetPath = TargetPath(contentRoot);
        if (!File.Exists(targetPath))
        {
            return new QuestModuleDeploymentStatus(
                QuestModuleDeploymentState.Missing,
                targetPath,
                ExpectedHash,
                null);
        }

        var actualHash = HashBytes(File.ReadAllBytes(targetPath));
        return new QuestModuleDeploymentStatus(
            string.Equals(actualHash, ExpectedHash, StringComparison.OrdinalIgnoreCase)
                ? QuestModuleDeploymentState.Current
                : QuestModuleDeploymentState.Outdated,
            targetPath,
            ExpectedHash,
            actualHash);
    }

    public static QuestModuleDeploymentStatus CopyToContentRoot(string contentRoot, bool overwrite)
    {
        var targetPath = TargetPath(contentRoot);
        if (File.Exists(targetPath) && !overwrite)
            throw new IOException($"QuestModule already exists: {targetPath}");

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllBytes(targetPath, TemplateBytes.Value);
        return GetStatus(contentRoot);
    }

    public static string TemplateText()
    {
        return System.Text.Encoding.UTF8.GetString(TemplateBytes.Value);
    }

    private static byte[] ReadTemplateBytes()
    {
        var assembly = typeof(QuestModuleDeployment).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {ResourceName}");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static string HashBytes(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
