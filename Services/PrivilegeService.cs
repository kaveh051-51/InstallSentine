namespace InstallSentinel.Services;

using InstallSentinel.Models;
using InstallSentinel.Services.Interfaces;
using InstallSentinel.Common.Helpers;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Security.Principal;

public sealed class PrivilegeService : IPrivilegeService
{
    public bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public Task<bool> RequireAdminAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunningAsAdmin())
            return Task.FromResult(true);

        return Task.FromResult(ElevateCurrentProcess());
    }

    public bool ElevateCurrentProcess()
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = string.Join(" ", Environment.GetCommandLineArgs().Skip(1).Select(a => $"\"{a}\""))
            };

            Process.Start(processInfo);
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User cancelled UAC prompt
            return false;
        }
        catch
        {
            return false;
        }
    }
}