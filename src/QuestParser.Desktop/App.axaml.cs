using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using QuestParser.Core;

namespace QuestParser.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = ShouldOpenVisualEditor(desktop.Args)
                ? new VisualEditorWindow(CreateWorkflow(), LoadSpecFromArgs(desktop.Args), ownsSpec: true)
                : new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static bool ShouldOpenVisualEditor(string[]? args)
    {
        return args?.Any(arg => string.Equals(arg, "--visual-editor", StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static QuestSpec? LoadSpecFromArgs(string[]? args)
    {
        if (args is null)
            return null;

        for (var index = 0; index < args.Length - 1; index++)
        {
            if (!string.Equals(args[index], "--spec", StringComparison.OrdinalIgnoreCase))
                continue;

            var path = args[index + 1];
            if (File.Exists(path))
                return QuestWorkflow.ReadSpecAsync(path).GetAwaiter().GetResult();

            return null;
        }

        return null;
    }

    private static QuestWorkflow CreateWorkflow()
    {
        var settings = QuestParserUiSettings.Load();
        return new QuestWorkflow(
            censusClient: CensusClientFactory.Create(settings.ToCensusOptions()),
            resolver: settings.CreateDatabaseResolver());
    }
}
