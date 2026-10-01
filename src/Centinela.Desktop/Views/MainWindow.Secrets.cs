using Centinela.Desktop.Services.Data;
using Centinela.Desktop.Services.Ui;

namespace Centinela.Desktop.Views;

/// <summary>
/// Without a keychain (Linux), the passwords are kept in Centinela's encrypted file: told once, after the cameras
/// are loaded (when the window first shows) or saved, while the window is on screen (shown, not minimized, not in the tray).
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
        // Hidden in the tray or minimized: told once the window is on screen.
        if (_closing || !WindowVisible) return;
        if (!FileSecretNotice.TryClaim(App.Platform.SecretsUseFileFallback, _vm.Settings.Settings)) return;
        _vm.Settings.Save();
        _ = MessageDialog.ShowAsync(this, FileSecretNotice.Text, FileSecretNotice.Title);
    }
}
