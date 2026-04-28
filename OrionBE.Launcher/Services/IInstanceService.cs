using OrionBE.Launcher.Models;

namespace OrionBE.Launcher.Services;

public interface IInstanceService
{
    Task<IReadOnlyList<InstanceSummary>> ListInstancesAsync(CancellationToken cancellationToken = default);
    Task<InstanceSummary?> GetAsync(string instanceFolderName, CancellationToken cancellationToken = default);
    Task<string> AllocateInstanceFolderNameAsync(string displayName, CancellationToken cancellationToken = default);
    Task SaveConfigAsync(string instanceFolderName, InstanceConfig config, CancellationToken cancellationToken = default);

    Task DeleteInstanceAsync(string instanceFolderName, CancellationToken cancellationToken = default);

    Task EnsureLauncherLayoutAsync(CancellationToken cancellationToken = default);
}
