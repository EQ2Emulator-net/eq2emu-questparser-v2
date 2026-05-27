using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using QuestParser.Core;

namespace QuestParser.Desktop;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly QuestWorkflow _workflow = new();

    public MainWindow()
    {
        InitializeComponent();
        ContentRootBox.Text = Defaults.ContentRoot;
        WireActions();
        AppendLog("Ready. Import or create a quest to start.");
        AppendLog(Defaults.HasDatabaseConfiguration
            ? "MariaDB configuration detected; DB references will be resolved automatically."
            : "MariaDB is not configured; DB references will remain as review TODOs.");
    }

    private void WireActions()
    {
        ImportButton.Click += async (_, _) => await RunAsync("Importing quest", ImportAsync);
        CreateButton.Click += async (_, _) => await RunAsync("Creating quest", CreateAsync);
        PreviewButton.Click += async (_, _) => await RunAsync("Previewing spec", PreviewSpecAsync);
        GenerateButton.Click += async (_, _) => await RunAsync("Generating files", GenerateAsync);
    }

    private async Task ImportAsync()
    {
        var result = await _workflow.ImportAsync(RequiredQuestName(), ContentRoot(), AuthorBox.Text ?? "");
        SpecPathBox.Text = result.Spec.Output.SpecPath;
        ShowResult(_workflow.Preview(result.Spec), "Imported quest spec");
    }

    private async Task CreateAsync()
    {
        var result = await _workflow.CreateAsync(RequiredQuestName(), ContentRoot(), AuthorBox.Text ?? "", OverwriteBox.IsChecked == true);
        SpecPathBox.Text = result.Spec.Output.SpecPath;
        ShowResult(result, "Created quest assets");
    }

    private async Task PreviewSpecAsync()
    {
        var spec = await QuestWorkflow.ReadSpecAsync(RequiredSpecPath());
        ShowResult(_workflow.Preview(spec), "Previewed quest spec");
    }

    private async Task GenerateAsync()
    {
        var result = await _workflow.GenerateAsync(RequiredSpecPath(), OverwriteBox.IsChecked == true);
        ShowResult(result, "Generated quest assets");
    }

    private async Task RunAsync(string status, Func<Task> action)
    {
        try
        {
            SetBusy(status + "...");
            await action();
            SetReady(status + " complete.");
        }
        catch (Exception ex)
        {
            SetReady("Failed.");
            AppendLog("ERROR: " + ex.Message);
        }
    }

    private void ShowResult(QuestWorkflowResult result, string title)
    {
        LuaBox.Text = result.Lua;
        SqlBox.Text = result.Sql;
        MissingBox.Text = result.MissingReport;
        SpecBox.Text = JsonSerializer.Serialize(result.Spec, JsonOptions);

        AppendLog(title);
        AppendLog($"Quest: {result.Spec.Quest.Name}");
        AppendLog($"Spec: {result.Spec.Output.SpecPath}");
        if (result.WrittenFiles.Count > 0)
        {
            AppendLog("Written files:");
            foreach (var file in result.WrittenFiles)
                AppendLog("  " + file);
        }

        if (result.Spec.Todos.Count > 0)
        {
            AppendLog("TODOs:");
            foreach (var todo in result.Spec.Todos)
                AppendLog("  " + todo);
        }
    }

    private string RequiredQuestName()
    {
        var value = (QuestNameBox.Text ?? "").Trim();
        if (value.Length == 0)
            throw new InvalidOperationException("Quest name is required.");
        return value;
    }

    private string RequiredSpecPath()
    {
        var value = (SpecPathBox.Text ?? "").Trim();
        if (value.Length == 0)
            throw new InvalidOperationException("Spec path is required.");
        return value;
    }

    private string ContentRoot()
    {
        var value = (ContentRootBox.Text ?? "").Trim();
        return value.Length == 0 ? Defaults.ContentRoot : value;
    }

    private void SetBusy(string text)
    {
        StatusText.Text = text;
        SetActionsEnabled(false);
        AppendLog(text);
    }

    private void SetReady(string text)
    {
        StatusText.Text = text;
        SetActionsEnabled(true);
        AppendLog(text);
    }

    private void SetActionsEnabled(bool enabled)
    {
        ImportButton.IsEnabled = enabled;
        CreateButton.IsEnabled = enabled;
        PreviewButton.IsEnabled = enabled;
        GenerateButton.IsEnabled = enabled;
    }

    private void AppendLog(string text)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        LogBox.Text += $"[{timestamp}] {text}{Environment.NewLine}";
    }
}
