using System.IO;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrionBE.Launcher.I18n;
using OrionBE.Launcher.Services;
using OrionBe.Router;
using OrionBe.ViewModel.Shared;

namespace OrionBe.ViewModel.Profile;

public partial class ProfileViewModel : MainWindowViewModelBase
{
    private readonly ILauncherProfileService _profile;
    private readonly IUiDialogService _dialogs;
    private readonly RouterHistory<MainWindowViewModelBase> _router;

    private Bitmap? _previewAvatarBitmap;

    [ObservableProperty]
    private string _nickname = "";

    [ObservableProperty]
    private string _tagline = "";

    [ObservableProperty]
    private IImage? _previewAvatar;

    [ObservableProperty]
    private bool _showAvatarPlaceholder = true;

    [ObservableProperty]
    private bool _showAvatarImage;

    [ObservableProperty]
    private int _taglineRemaining;

    [ObservableProperty]
    private string _taglineFooterText = "";

    public ProfileViewModel(
        ILauncherProfileService profile,
        IUiDialogService dialogs,
        RouterHistory<MainWindowViewModelBase> router)
    {
        _profile = profile;
        _dialogs = dialogs;
        _router = router;
    }

    public override Task OnNavigatedToAsync(CancellationToken cancellationToken)
    {
        LoadFromService();
        return Task.CompletedTask;
    }

    partial void OnTaglineChanged(string value) => RefreshTaglineRemaining();

    private void LoadFromService()
    {
        var s = _profile.GetSnapshot();
        Nickname = s.Nickname ?? "";
        Tagline = s.Tagline ?? "";
        RefreshTaglineRemaining();
        ApplyPhotoPreview(s.AvatarFullPath);
    }

    private void RefreshTaglineRemaining()
    {
        TaglineRemaining = LauncherProfileService.MaxTaglineLength - Tagline.Length;
        TaglineFooterText = string.Format(
            Localizer.Instance["profile_tagline_remaining"],
            TaglineRemaining);
    }

    private void ApplyPhotoPreview(string? path)
    {
        _previewAvatarBitmap?.Dispose();
        _previewAvatarBitmap = null;
        PreviewAvatar = null;

        var ok = !string.IsNullOrEmpty(path) && File.Exists(path);
        if (!ok)
        {
            ShowAvatarImage = false;
            ShowAvatarPlaceholder = true;
            return;
        }

        try
        {
            using var stream = File.OpenRead(path!);
            _previewAvatarBitmap = new Bitmap(stream);
            PreviewAvatar = _previewAvatarBitmap;
            ShowAvatarImage = true;
            ShowAvatarPlaceholder = false;
        }
        catch
        {
            ShowAvatarImage = false;
            ShowAvatarPlaceholder = true;
        }
    }

    [RelayCommand]
    private async Task PickPhotoAsync(CancellationToken cancellationToken)
    {
        var path = await _dialogs.PickImageFileAsync(cancellationToken).ConfigureAwait(true);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var saved = _profile.InstallAvatarFromUserFile(path);
        ApplyPhotoPreview(saved);
    }

    [RelayCommand]
    private void RemovePhoto()
    {
        _profile.ClearAvatar();
        ApplyPhotoPreview(null);
    }

    [RelayCommand]
    private void Save()
    {
        _profile.SaveNicknameAndTagline(Nickname, Tagline);
        _router.Navigate("hub", null, Localizer.Instance["titles_hub"]);
    }

    [RelayCommand]
    private void Cancel()
    {
        _router.Navigate("hub", null, Localizer.Instance["titles_hub"]);
    }
}
