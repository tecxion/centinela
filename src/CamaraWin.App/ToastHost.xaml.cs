using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace CamaraWin.App;

/// <summary>Stack of self-dismissing notices in the window's bottom-right corner; clicking one opens the error log.</summary>
public partial class ToastHost : UserControl
{
    const int MaxVisible = 4;

    public ToastHost() => InitializeComponent();

    public event Action? OpenLogRequested;

    public void Show(Toast toast)
    {
        var close = new Button { Content = "✕", Width = 24, Height = 24, Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Top };
        var text = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        text.Children.Add(new TextBlock { Text = toast.Title, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
        if (toast.Message.Length > 0)
            text.Children.Add(new TextBlock { Text = toast.Message, Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        var row = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        row.Children.Add(close);
        row.Children.Add(text);
        var card = new Border
        {
            Child = row, Padding = new Thickness(12), Margin = new Thickness(0, 8, 0, 0), CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(toast.IsRecovery ? Color.FromRgb(0x1E, 0x4D, 0x2B) : Color.FromRgb(0x5A, 0x1F, 0x1F)),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(toast.IsRecovery ? 4 : 8) };
        void Remove() { timer.Stop(); Stack.Children.Remove(card); }
        timer.Tick += (_, _) => Remove();
        close.Click += (_, e) => { e.Handled = true; Remove(); };
        card.MouseLeftButtonUp += (_, _) => { Remove(); OpenLogRequested?.Invoke(); };
        Stack.Children.Add(card);
        while (Stack.Children.Count > MaxVisible) Stack.Children.RemoveAt(0);
        timer.Start();
    }
}
