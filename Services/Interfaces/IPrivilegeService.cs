namespace InstallSentinel.Services.Interfaces;

using InstallSentinel.Models;

public interface IPrivilegeService
{
    bool IsRunningAsAdmin();
    Task<bool> RequireAdminAsync(CancellationToken cancellationToken = default);
    bool ElevateCurrentProcess();
}