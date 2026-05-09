namespace OrionBE.Launcher.Services;

public interface IStartupDependencyCheckService
{
    Task<StartupDependencyCheckReport> RunIfFirstLaunchAsync(CancellationToken cancellationToken = default);
}
