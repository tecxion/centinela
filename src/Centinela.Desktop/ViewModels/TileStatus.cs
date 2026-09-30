using Centinela.Core;
using Centinela.Desktop.Services.Errors;
using Centinela.Media;

namespace Centinela.Desktop.ViewModels;

/// <summary>The text over a tile's video for its session state and last error (kept while retrying).</summary>
static class TileStatus
{
    public static string Text(SessionState state, StreamErrorKind? error, Camera camera)
    {
        if (state == SessionState.Playing) return "";
        if (state == SessionState.AuthFailed) return ErrorText.Translate(camera, StreamErrorKind.AuthFailed).Short;
        if (error is { } kind && state is SessionState.Connecting or SessionState.Reconnecting)
        {
            var text = ErrorText.Translate(camera, kind).Short;
            return kind == StreamErrorKind.AuthFailed ? text : $"{text} · reintentando";
        }
        return state switch
        {
            SessionState.Connecting => "Conectando…",
            SessionState.Reconnecting => "Reconectando…",
            SessionState.Stopped => "Detenida",
            _ => "",
        };
    }
}
