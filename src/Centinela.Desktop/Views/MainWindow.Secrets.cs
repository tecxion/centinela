using Centinela.Desktop.Services.Data;
using Centinela.Desktop.Services.Ui;

namespace Centinela.Desktop.Views;

/// <summary>
/// Without a keychain (Linux), the passwords are kept in Centinela's encrypted file: told once, after the cameras
/// are loaded (when the window first shows) or saved, while the window is on screen.
/// </summary>
public partial class MainWindow
{
    void InitFileSecretNotice()
    {
        // Posted, so the window is fully open before the message shows over it.
        Opened += (_, _) => UiThread.Post(ShowFileSecretNoticeOnce);
        // Raised outside the repository's try: must not throw.
        _vm.Cameras.Saved += ShowFileSecretNoticeOnce;
    }

    void ShowFileSecretNoticeOnce()
    {
        // Hidden in the tray: told when the window shows.
        if (_closing || !IsVisible || IsHiddenInTray) return;
        if (!FileSecretNotice.TryClaim(App.Platform.SecretsUseFileFallback, _vm.Settings.Settings)) return;
        _vm.Settings.Save();
        _ = MessageDialog.ShowAsync(this, FileSecretNotice.Text, FileSecretNotice.Title);
    }
}
