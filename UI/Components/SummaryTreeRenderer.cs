namespace InstallSentinel.UI.Components;

using InstallSentinel.Models;
using InstallSentinel.Models.Enums;
using InstallSentinel.Common.Helpers;
using Spectre.Console;

public sealed class SummaryTreeRenderer
{
    public static Tree RenderProcessTree(ProcessNode root)
    {
        var tree = new Tree(new Markup($"[bold cyan]{root.ProcessName}[/] [dim](PID: {root.ProcessId})[/]"))
        {
            Style = new Style(Color.Purple)
        };

        var rootNode = tree.AddNode(new Markup(BuildNodeText(root)));
        foreach (var child in root.Children.OrderBy(c => c.ProcessId))
        {
            RenderNode(rootNode, child);
        }
        return tree;
    }

    private static void RenderNode(TreeNode parentNode, ProcessNode node)
    {
        var childNode = parentNode.AddNode(new Markup(BuildNodeText(node)));

        foreach (var child in node.Children.OrderBy(c => c.ProcessId))
        {
            RenderNode(childNode, child);
        }
    }

    private static string BuildNodeText(ProcessNode node)
    {
        var threatColor = node.ThreatStatus switch
        {
            ThreatStatus.Malicious => "red",
            ThreatStatus.Suspicious => "yellow",
            ThreatStatus.Benign => "green",
            _ => "grey"
        };

        var nodeText = $"[bold]{node.ProcessName}[/] [dim](PID: {node.ProcessId})[/]";
        if (node.ThreatStatus != ThreatStatus.NotScanned)
        {
            nodeText += $" [{threatColor}]{node.ThreatStatus}[/]";
        }
        return nodeText;
    }

    public static Panel RenderSummary(MonitoringReport report)
    {
        var grid = new Grid();
        grid.AddColumn(new GridColumn().Width(30));
        grid.AddColumn(new GridColumn());

        grid.AddRow("[bold white]Installer Path:[/]", $"[cyan]{PathSanitizer.TruncatePath(report.InstallerPath, 60)}[/]");
        grid.AddRow("[bold white]SHA256:[/]", $"[grey]{report.InstallerSha256}[/]");
        grid.AddRow("[bold white]Start Time:[/]", $"[green]{report.StartTime:yyyy-MM-dd HH:mm:ss}[/]");
        grid.AddRow("[bold white]End Time:[/]", $"[red]{report.EndTime:yyyy-MM-dd HH:mm:ss}[/]");
        grid.AddRow("[bold white]Duration:[/]", $"[yellow]{report.Duration:hh\\:mm\\:ss}[/]");
        grid.AddRow("", "");
        grid.AddRow("[bold white]Total Events:[/]", $"[bold cyan]{report.AllEvents.Count:N0}[/]");
        grid.AddRow("[bold white]File System Changes:[/]", $"[green]{report.TotalFileSystemChanges:N0}[/] (Created: [green]{report.AllEvents.Count(e => e.Category == EventCategory.FileSystem && e.Action == ActionType.Create)}[/], Modified: [yellow]{report.AllEvents.Count(e => e.Category == EventCategory.FileSystem && (e.Action == ActionType.Modify || e.Action == ActionType.Write))}[/], Deleted: [red]{report.AllEvents.Count(e => e.Category == EventCategory.FileSystem && e.Action == ActionType.Delete)}[/])");
        grid.AddRow("[bold white]Registry Changes:[/]", $"[yellow]{report.TotalRegistryChanges:N0}[/]");
        grid.AddRow("[bold white]Process Events:[/]", $"[cyan]{report.TotalProcessEvents:N0}[/]");
        grid.AddRow("", "");

        if (report.VirusTotalReport != null)
        {
            var vt = report.VirusTotalReport;
            var vtColor = vt.ThreatStatus switch
            {
                ThreatStatus.Malicious => "red",
                ThreatStatus.Suspicious => "yellow",
                ThreatStatus.Benign => "green",
                _ => "grey"
            };
            grid.AddRow("[bold white]VirusTotal:[/]", $"[bold {vtColor}]{vt.ThreatStatus}[/] ({vt.Positives}/{vt.Total})");
        }

        if (!string.IsNullOrEmpty(report.RollbackScriptPath))
        {
            grid.AddRow("", "");
            grid.AddRow("[bold yellow]Rollback Script:[/]", $"[cyan]{report.RollbackScriptPath}[/]");
        }

        return new Panel(grid)
            .Header("[bold white]Execution Summary[/]")
            .BorderColor(Color.Purple)
            .RoundedBorder();
    }

    public static Panel RenderRollbackInfo(MonitoringReport report)
    {
        var grid = new Grid();
        grid.AddColumn(new GridColumn().Width(25));
        grid.AddColumn(new GridColumn());

        grid.AddRow("[bold white]Rollback Script:[/]", $"[cyan]{report.RollbackScriptPath}[/]");
        grid.AddRow("[bold white]Generated:[/]", $"[green]{DateTime.Now:yyyy-MM-dd HH:mm:ss}[/]");
        grid.AddRow("[bold white]Actions:[/]", $"[yellow]{report.AllEvents.Count}[/] rollback actions");

        return new Panel(grid)
            .Header("[bold yellow]Rollback Ready[/]")
            .BorderColor(Color.Yellow)
            .RoundedBorder();
    }
}
