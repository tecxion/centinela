using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Centinela.Desktop.Services.Notices;

namespace Centinela.Desktop.Views;

/// <summary>Stack of self-dismissing notices in the window's bottom-right corner; clicking one runs its action or opens the error log.</summary>
public partial class ToastHost : UserControl
{
    const int MaxVisible = 4;
    static readonly IBrush MessageBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
    static readonly IBrush ActionBrush = new SolidColorBrush(Color.FromRgb(0x9C, 0xD0, 0xFF));

    public ToastHost() => InitializeComponent();

    public event Action? OpenLogRequested;

    public void Show(Toast toast)
    {
        var close = new Button
        {
            Content = "✕", Width = 24, Height = 24, Padding = new Thickness(0), Background = Brushes.Transparent,
            Foreground = Brushes.White, BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Top,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };
        var text = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        text.Children.Add(new TextBlock
        {
            Text = toast.Title, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap,
        });
        if (toast.Message.Length > 0)
            text.Children.Add(new TextBlock
            {
                Text = toast.Message, Foreground = MessageBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
            });
        if (toast.ActionText is { } actionText)
            text.Children.Add(new TextBlock
            {
                Text = actionText, Foreground = ActionBrush, TextDecorations = TextDecorations.Underline,
                Margin = new Thickness(0, 6, 0, 0),
            });
        var row = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        row.Children.Add(close);
        row.Children.Add(text);
        var card = new Border
        {
            Child = row, Padding = new Thickness(12), Margin = new Thickness(0, 8, 0, 0), CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(toast.Style switch
            {
                ToastStyle.Recovery => Color.FromRgb(0x1E, 0x4D, 0x2B),
                ToastStyle.Info => Color.FromRgb(0x1F, 0x3A, 0x5A),
                _ => Color.FromRgb(0x5A, 0x1F, 0x1F),
            }),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        var lifetime = toast.Style switch { ToastStyle.Recovery => 4, ToastStyle.Info => 12, _ => 8 };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(lifetime) };
        void Remove()
        {
            timer.Stop();
            Stack.Children.Remove(card);
        }
        timer.Tick += (_, _) => Remove();
        // The button handles its own pointer release, so a click on «✕» never reaches the card.
        close.Click += (_, e) =>
        {
            e.Handled = true;
            Remove();
        };
        card.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            Remove();
            if (toast.OnAction is { } action) action();
            else OpenLogRequested?.Invoke();
        };
        Stack.Children.Add(card);
        while (Stack.Children.Count > MaxVisible) Stack.Children.RemoveAt(0);
        timer.Start();
    }
}
