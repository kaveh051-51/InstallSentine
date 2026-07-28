namespace InstallSentinel.UI.Components;
using InstallSentinel.UI.Components;
using InstallSentinel.Models;
using InstallSentinel.Models.Enums;
using InstallSentinel.Common.Helpers;
using Spectre.Console;
using System.Text;

public sealed class LiveEventTable(int maxRows = 200)
{
    private readonly Table _table = new Table()
            .RoundedBorder()
            .BorderColor(Color.Purple)
            .AddColumn(new TableColumn("[bold cyan]Time[/]").Width(10).NoWrap())
            .AddColumn(new TableColumn("[bold cyan]PID[/]").Width(8).RightAligned())
            .AddColumn(new TableColumn("[bold cyan]Category[/]").Width(12))
            .AddColumn(new TableColumn("[bold cyan]Action[/]").Width(14))
            .AddColumn(new TableColumn("[bold cyan]Target Path[/]").Width(80).NoWrap());
    private readonly List<SystemEvent> _events = [];
    private readonly object _lock = new();
    private int _totalEvents = 0;
    private int _fileCreated = 0;
    private int _fileModified = 0;
    private int _fileDeleted = 0;
    private int _registryChanges = 0;
    private int _processEvents = 0;
    private int _imageLoads = 0;
    private DateTime _startTime = DateTime.UtcNow;
    private int _rootPid = 0;
    private string _processName = "";

    public void SetContext(int rootPid, string processName)
    {
        _rootPid = rootPid;
        _processName = processName;
        _startTime = DateTime.UtcNow;
        ResetCounters();
    }

    public void AddEvent(SystemEvent evt)
    {
        lock (_lock)
        {
            _events.Insert(0, evt);
            if (_events.Count > 200)
                _events.RemoveAt(_events.Count - 1);

            _totalEvents++;
            UpdateCounters(evt);
        }
    }

    private void UpdateCounters(SystemEvent evt)
    {
        switch (evt.Category)
        {
            case EventCategory.FileSystem:
                switch (evt.Action)
                {
                    case ActionType.Create: _fileCreated++; break;
                    case ActionType.Modify:
                    case ActionType.Write: _fileModified++; break;
                    case ActionType.Delete: _fileDeleted++; break;
                }
                break;
            case EventCategory.Registry:
                _registryChanges++; break;
            case EventCategory.Process:
                _processEvents++; break;
            case EventCategory.ImageLoad:
                _imageLoads++; break;
        }
    }

    private void ResetCounters()
    {
        _totalEvents = 0;
        _fileCreated = _fileModified = _fileDeleted = 0;
        _registryChanges = _processEvents = _imageLoads = 0;
    }

    public Table GetTable()
    {
        lock (_lock)
        {
            var table = new Table()
                .RoundedBorder()
                .BorderColor(Color.Purple)
                .AddColumn(new TableColumn("[bold cyan]Time[/]").Width(10).NoWrap())
                .AddColumn(new TableColumn("[bold cyan]PID[/]").Width(8).RightAligned())
                .AddColumn(new TableColumn("[bold cyan]Category[/]").Width(12))
                .AddColumn(new TableColumn("[bold cyan]Action[/]").Width(14))
                .AddColumn(new TableColumn("[bold cyan]Target Path[/]").Width(80).NoWrap());

            var displayEvents = _events.Take(50).ToList();
            foreach (var evt in displayEvents)
            {
                var time = evt.Timestamp.ToLocalTime().ToString("HH:mm:ss");
                var category = GetCategoryMarkup(evt.Category);
                var action = GetActionMarkup(evt.Action);
                var path = PathSanitizer.TruncatePath(evt.TargetPath, 80);

                table.AddRow(time, evt.ProcessId.ToString(), category, action, path);
            }

            return table;
        }
    }

    public Panel GetStatusPanel()
    {
        lock (_lock)
        {
            var uptime = DateTime.UtcNow - _startTime;
            var uptimeStr = uptime.ToString(@"hh\:mm\:ss");

            var grid = new Grid();
            grid.AddColumn(new GridColumn().Width(30));
            grid.AddColumn(new GridColumn().Width(20));
            grid.AddColumn(new GridColumn().Width(20));
            grid.AddColumn(new GridColumn());

            grid.AddRow(
                $"[bold cyan]Live Tracing[/] [dim]PID: {_rootPid} ({_processName})[/]",
                $"[bold white]Uptime: {uptimeStr}[/]",
                $"[bold white]Events: {_totalEvents}[/]",
                "");

            grid.AddRow(
                $"[green]Created: {_fileCreated}[/]",
                $"[yellow]Modified: {_fileModified}[/]",
                $"[red]Deleted: {_fileDeleted}[/]",
                $"[purple]Registry: {_registryChanges}[/]");

            grid.AddRow(
                $"[cyan]Process: {_processEvents}[/]",
                $"[blue]Image Loads: {_imageLoads}[/]",
                "",
                "");

            return new Panel(grid)
                .Header("[bold white]Status[/]")
                .BorderColor(Color.Purple)
                .RoundedBorder();
        }
    }

    private static string GetCategoryMarkup(EventCategory category) => category switch
    {
        EventCategory.FileSystem => "[green]FileSystem[/]",
        EventCategory.Registry => "[yellow]Registry[/]",
        EventCategory.Process => "[cyan]Process[/]",
        EventCategory.ImageLoad => "[blue]ImageLoad[/]",
        EventCategory.Thread => "[grey]Thread[/]",
        EventCategory.Network => "[purple]Network[/]",
        _ => "[white]Unknown[/]"
    };

    private static string GetActionMarkup(ActionType action) => action switch
    {
        ActionType.Create => "[bold green]CREATED[/]",
        ActionType.Modify => "[bold yellow]MODIFIED[/]",
        ActionType.Write => "[bold yellow]WRITTEN[/]",
        ActionType.Delete => "[bold red]DELETED[/]",
        ActionType.Rename => "[bold purple]RENAMED[/]",
        ActionType.Read => "[grey]READ[/]",
        ActionType.SetValue => "[yellow]SET VALUE[/]",
        ActionType.DeleteValue => "[red]DEL VALUE[/]",
        ActionType.CreateKey => "[green]CREATE KEY[/]",
        ActionType.DeleteKey => "[red]DELETE KEY[/]",
        ActionType.Start => "[bold cyan]STARTED[/]",
        ActionType.Exit => "[bold red]EXITED[/]",
        ActionType.Load => "[bold blue]LOADED[/]",
        ActionType.Unload => "[grey]UNLOADED[/]",
        _ => "[white]UNKNOWN[/]"
    };
}