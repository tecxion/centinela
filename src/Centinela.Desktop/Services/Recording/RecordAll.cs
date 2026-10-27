using Centinela.Core;

namespace Centinela.Desktop.Services.Recording;

/// <summary>Which cameras «Grabar todas» records: the ones on screen (toolbar) or every camera (tray menu).</summary>
static class RecordAll
{
    /// <summary>In the order of <paramref name="all"/>; ids on screen that are not cameras any more are ignored.</summary>
    public static IReadOnlyList<Guid> Targets(IEnumerable<Camera> all, IEnumerable<Guid> onScreen, bool allCameras)
    {
        if (allCameras) return [.. all.Select(c => c.Id)];
        var shown = onScreen.ToHashSet();
        return [.. all.Select(c => c.Id).Where(shown.Contains)];
    }
}
