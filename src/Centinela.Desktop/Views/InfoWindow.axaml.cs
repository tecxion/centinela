using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Centinela.Desktop.Services.Info;
using Centinela.Desktop.Services.Ui;

namespace Centinela.Desktop.Views;

/// <summary>Read-only document window (manual, licence, support) whose links open in the default handler.</summary>
public partial class InfoWindow : Window
{
    const string MonospaceFonts = "Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace";

    // Parameterless constructor for the XAML loader and designer.
    public InfoWindow() => InitializeComponent();

    internal InfoWindow(string title, IReadOnlyList<InfoBlock> blocks) : this()
    {
        Title = title;
        foreach (var block in blocks) Blocks.Children.Add(ToControl(block));
    }

    Control ToControl(InfoBlock block) => block switch
    {
        InfoHeading h => new TextBlock
        {
            Text = h.Text, FontWeight = FontWeight.Bold, FontSize = 18, Margin = new Avalonia.Thickness(0, 16, 0, 6),
            TextWrapping = TextWrapping.Wrap,
        },
        InfoParagraph p => Paragraph(p),
        InfoLink l => Link(l),
        InfoCode c => new SelectableTextBlock { Text = c.Text, FontFamily = new FontFamily(MonospaceFonts), FontSize = 12 },
        _ => throw new ArgumentOutOfRangeException(nameof(block)),
    };

    static SelectableTextBlock Paragraph(InfoParagraph p)
    {
        var text = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14 };
        text.Inlines = [];
        if (p.Lead is not null) text.Inlines.Add(new Run(p.Lead) { FontWeight = FontWeight.Bold });
        text.Inlines.Add(new Run(p.Text));
        return text;
    }

    HyperlinkButton Link(InfoLink l)
    {
        var button = new HyperlinkButton { Content = l.Text, Padding = new Avalonia.Thickness(0) };
        button.Click += (_, _) => _ = Links.OpenAsync(this, l.Uri);
        return button;
    }
}
