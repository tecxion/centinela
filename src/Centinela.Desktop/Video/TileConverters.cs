using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Centinela.Desktop.Video;

/// <summary>Dimmed video while not playing (as in the Windows version).</summary>
static class VideoOpacity
{
    public static readonly IValueConverter Converter =
        new FuncValueConverter<bool, double>(playing => playing ? 1 : 0.4);
}

/// <summary>Stats in orange when the stream is not delivering frames.</summary>
static class StatsBrush
{
    public static readonly IValueConverter Converter =
        new FuncValueConverter<bool, IBrush>(stale => stale ? Brushes.Orange : Brushes.White);
}
