using Centinela.Core;

namespace Centinela.Desktop.Services.Data;

/// <summary>The settings in memory and on disk.</summary>
sealed class SettingsRepository(SettingsStore store)
{
    public AppSettings Settings { get; } = store.Load();

    /// <summary>Losing a preference (window placement, layout) must never interrupt the user or block closing.</summary>
    public void Save()
    {
        try
        {
            store.Save(Settings);
        }
        catch (Exception)
        {
            // Ignored on purpose.
        }
    }
}
