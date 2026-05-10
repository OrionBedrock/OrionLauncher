using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OrionBe.Extensions;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrionBE.Launcher.I18n;
using OrionBe.Router;
using OrionBe.ViewModel.Shared;
using OrionBE.Launcher.Services;
using Umbra.Router.Core.Events;

namespace OrionBe.ViewModel;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "";

    [ObservableProperty] private Control _control = null!;

    [ObservableProperty] private bool _isActiveHome = false;
    [ObservableProperty] private bool _isActiveGame = false;
    [ObservableProperty] private bool _isActiveMods = false;

    [ObservableProperty] private bool _isActiveSettings = false;
    [ObservableProperty] private bool _isActiveProfile = false;

    [ObservableProperty] private bool _showProfileAvatarImage;
    [ObservableProperty] private bool _showProfilePlaceholder = true;

    /// <summary>Sidebar avatar; loaded from disk as <see cref="Bitmap"/> (reliable on Linux vs. async HTTP loader + file URIs).</summary>
    [ObservableProperty]
    private IImage? _profileSidebarAvatar;

    [ObservableProperty] private string _profilePrimaryLabel = "";
    [ObservableProperty] private string _profileSecondaryLabel = "";
    [ObservableProperty] private string _profileAreaToolTip = "";

    private readonly RouterHistory<MainWindowViewModelBase> _router;
    private readonly ILauncherProfileService _launcherProfile;

    private Bitmap? _profileSidebarAvatarBitmap;

    public MainWindowViewModel(
        RouterHistory<MainWindowViewModelBase> router,
        ILauncherProfileService launcherProfile)
    {
        _router = router;
        _launcherProfile = launcherProfile;

        _router.TitleChanged += x => _title = x;
        _router.PageChanged += OnPageChange;

        Localizer.Instance.CultureChanged += OnCultureChanged;

        ApplyProfileChromeLabels();

        _router.Navigate("hub", null, Localizer.Instance["titles_hub"]);
    }

    private void OnCultureChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(ApplyProfileChromeLabels);

    /// <summary>Sidebar local profile: nickname, tagline, optional avatar.</summary>
    private void ApplyProfileChromeLabels()
    {
        var snap = _launcherProfile.GetSnapshot();
        var nick = string.IsNullOrWhiteSpace(snap.Nickname)
            ? Localizer.Instance["profile_default_name"]
            : snap.Nickname.Trim();

        ProfilePrimaryLabel = nick;

        if (!string.IsNullOrWhiteSpace(snap.Tagline))
        {
            ProfileSecondaryLabel = snap.Tagline.Trim();
        }
        else
        {
            ProfileSecondaryLabel = Localizer.Instance["profile_sidebar_hint"];
        }

        ProfileAreaToolTip = Localizer.Instance["profile_sidebar_tooltip"];

        SetSidebarAvatar(snap.AvatarFullPath);
    }

    private void SetSidebarAvatar(string? path)
    {
        _profileSidebarAvatarBitmap?.Dispose();
        _profileSidebarAvatarBitmap = null;
        ProfileSidebarAvatar = null;

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            ShowProfileAvatarImage = false;
            ShowProfilePlaceholder = true;
            return;
        }

        try
        {
            using var stream = File.OpenRead(path);
            _profileSidebarAvatarBitmap = new Bitmap(stream);
            ProfileSidebarAvatar = _profileSidebarAvatarBitmap;
            ShowProfileAvatarImage = true;
            ShowProfilePlaceholder = false;
        }
        catch
        {
            ShowProfileAvatarImage = false;
            ShowProfilePlaceholder = true;
        }
    }

    private void OnPageChange(object? sender, NavigationResultEventArgs<Control> e)
    {
        if (e.Context == null)
        {
            return;
        }

        IsActiveHome = e.Context.IsActive("hub");
        IsActiveGame = e.Context.IsActive("game");
        IsActiveMods = e.Context.IsActive("mods");
        IsActiveSettings = e.Context.IsActive("settings");
        IsActiveProfile = e.Context.IsActive("profile");

        Control = e.Page;

        ApplyProfileChromeLabels();
    }

    [RelayCommand]
    private void OpenProfileEditor()
    {
        _router.Navigate("profile", null, Localizer.Instance["titles_profile"]);
    }

    [RelayCommand]
    private void NavigatePage(string page)
    {
        _router.Navigate(page, null, Localizer.Instance[$"titles_{page}"]);
    }

    /// <summary>Opens settings; if already on settings, navigates back to the hub.</summary>
    [RelayCommand]
    private void NavigateSettings()
    {
        if (IsActiveSettings)
        {
            NavigatePage("hub");
        }
        else
        {
            NavigatePage("settings");
        }
    }

    [RelayCommand]
    private static void OpenGitHub()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/OrionBedrock/OrionLauncher",
            UseShellExecute = true,
        });
    }
}
