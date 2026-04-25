namespace OrionBE.Launcher.Services;

public interface IXvdToolService
{
    /// <summary>Ensures <see href="https://github.com/AmethystAPI/xvdtool">xvdtool</see> is present under cache and returns the executable path.</summary>
    Task<string?> EnsureToolAsync(CancellationToken cancellationToken = default);

    /// <summary>Decrypt in-place using <c>-nd -eu -cik ... -cikdata ...</c>.</summary>
    Task DecryptMsixvcInPlaceAsync(string xvdtoolPath, string msixvcPath, CancellationToken cancellationToken = default);

    /// <summary>Extract package to folder using <c>-nd -xf</c>.</summary>
    Task ExtractMsixvcAsync(string xvdtoolPath, string msixvcPath, string outputFolder, CancellationToken cancellationToken = default);
}
