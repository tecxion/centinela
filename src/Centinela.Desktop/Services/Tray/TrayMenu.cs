using Centinela.Core.Platform;

namespace Centinela.Desktop.Services.Tray;

/// <summary>The tray menu: «Abrir», «Grabar todas» / «Detener todas», the auto start check, a separator and «Salir».</summary>
static class TrayMenu
{
    /// <param name="autoStartAvailable">False leaves out the auto start check (test runs never touch the system).</param>
    /// <param name="setAutoStart">Called with the state the user asked for (the opposite of the current check).</param>
    public static IReadOnlyList<TrayMenuItem> Build(bool anyRecording, bool autoStartAvailable, bool autoStartEnabled,
        OsKind os, Action open, Action recordAll, Action<bool> setAutoStart, Action exit)
    {
        var items = new List<TrayMenuItem>
        {
            new("Abrir", open),
            new(anyRecording ? "Detener todas" : "Grabar todas", recordAll),
        };
        if (autoStartAvailable)
            items.Add(new(TrayTexts.AutoStartLabel(os), () => setAutoStart(!autoStartEnabled), Checkable: true,
                Checked: autoStartEnabled));
        items.Add(new(null));
        items.Add(new("Salir", exit));
        return items;
    }
}
