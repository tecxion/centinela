using Centinela.Desktop.Services.Notices;

namespace Centinela.Desktop.Tests;

public class NoticeRouterTests
{
    readonly List<Toast> _inWindow = [];
    readonly List<(string Title, string Text, Action? OnClick)> _system = [];

    NoticeRouter Router(bool hidden) =>
        new(() => hidden, _inWindow.Add, (title, text, onClick) => _system.Add((title, text, onClick)));

    [Fact]
    public void Hidden_in_the_tray_shows_a_system_notice_with_the_action()
    {
        Action action = () => { };
        Router(hidden: true).Show(new Toast("Título", "Mensaje", ToastStyle.Info, "Ver", action));
        Assert.Empty(_inWindow);
        var notice = Assert.Single(_system);
        Assert.Equal("Título", notice.Title);
        Assert.Equal("Mensaje", notice.Text);
        Assert.Same(action, notice.OnClick);
    }

    [Fact]
    public void Hidden_without_action_passes_none()
    {
        Router(hidden: true).Show(new Toast("Error", "Detalle", ToastStyle.Error));
        Assert.Null(Assert.Single(_system).OnClick);
    }

    [Fact]
    public void Visible_shows_it_in_the_window()
    {
        var toast = new Toast("Título", "Mensaje", ToastStyle.Recovery);
        Router(hidden: false).Show(toast);
        Assert.Empty(_system);
        Assert.Same(toast, Assert.Single(_inWindow));
    }

    [Fact]
    public void Asks_whether_hidden_on_every_notice()
    {
        var hidden = false;
        var router = new NoticeRouter(() => hidden, _inWindow.Add, (t, m, a) => _system.Add((t, m, a)));
        router.Show(new Toast("a", "", ToastStyle.Info));
        hidden = true;
        router.Show(new Toast("b", "", ToastStyle.Info));
        Assert.Single(_inWindow);
        Assert.Single(_system);
    }
}
