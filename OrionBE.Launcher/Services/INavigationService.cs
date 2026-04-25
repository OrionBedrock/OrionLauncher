using OrionBE.Launcher.ViewModels;

namespace OrionBE.Launcher.Services;

public enum SidebarSection
{
    Main,
    BrowseMods,
}

public interface INavigationService
{
    event EventHandler? Navigated;

    ViewModelBase? CurrentViewModel { get; }

    SidebarSection ActiveSection { get; }

    void SetRootHome();

    void SetRootBrowseMods();

    void PushAddInstance();

    void PushInstanceSettings(string instanceFolderName);

    void PushModDetails(string modId);

    bool GoBack();
}
