namespace OrionBE.Launcher.Services;

public interface IXvdToolService
{
    Task<string?> EnsureToolAsync(CancellationToken cancellationToken = default);

    Task DecryptMsixvcInPlaceAsync(string xvdtoolPath, string msixvcPath, CancellationToken cancellationToken = default);

    Task ExtractMsixvcAsync(string xvdtoolPath, string msixvcPath, string outputFolder, CancellationToken cancellationToken = default);
}
