using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Centinela.Desktop.Views;

/// <summary>Small modal message: «Aceptar», or «Sí» / «No». Without an owner it shows as its own window.</summary>
static class MessageDialog
{
    public static async Task ShowAsync(Window? owner, string text, string title = "Centinela") => await Build(owner, text, title, confirm: false);

    /// <param name="defaultNo">«No» is the default (and focused) button, for risky actions: Enter or Space does not confirm.</param>
    public static async Task<bool> ConfirmAsync(Window owner, string text, bool defaultNo = false, string title = "Centinela") =>
        await Build(owner, text, title, confirm: true, defaultNo);

    static Task<bool> Build(Window? owner, string text, string title, bool confirm, bool defaultNo = false)
    {
        var result = new TaskCompletionSource<bool>();
        var window = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            MaxWidth = 520,
            CanResize = false,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        Button Add(string label, bool value, bool isDefault = false)
        {
            var button = new Button { Content = label, MinWidth = 80, IsDefault = isDefault, IsCancel = !value || !confirm };
            button.Click += (_, _) =>
            {
                result.TrySetResult(value);
                window.Close();
            };
            buttons.Children.Add(button);
            return button;
        }
        if (confirm)
        {
            Add("Sí", true, isDefault: !defaultNo);
            var no = Add("No", false, isDefault: defaultNo);
            if (defaultNo) window.Opened += (_, _) => no.Focus();
        }
        else Add("Aceptar", true, isDefault: true);

        window.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children = { new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, buttons },
        };
        window.Closed += (_, _) => result.TrySetResult(false);
        if (owner is null) window.Show();
        else _ = window.ShowDialog(owner);
        return result.Task;
    }
}
