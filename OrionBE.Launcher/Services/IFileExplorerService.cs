namespace OrionBE.Launcher.Services;

public interface IFileExplorerService
{
    /// <summary>Abre a pasta do caminho, ou a pasta do ficheiro (seleção no Windows com /select, quando suportado).</summary>
    void RevealInFileManager(string? path);
}
