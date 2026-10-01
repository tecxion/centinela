using Centinela.Core;
using Centinela.Desktop.ViewModels;
using Centinela.Media;

namespace Centinela.Desktop.Tests;

/// <summary>What the tile's buttons and indicators show. The camera has no URL, so there is no session (no network).</summary>
public class TileDisplayTests
{
    static TileViewModel NewTile(bool manage = true) =>
        new(new Camera { Name = "Patio", Brand = Brand.Custom }, StreamKind.Main, manage, null);

    [Fact]
    public void The_test_tile_has_no_session() => Assert.Null(NewTile().Session);

    [Fact]
    public void Quality_texts()
    {
        var tile = NewTile();
        tile.SetLowQuality(true);
        Assert.Equal("SD", tile.QualityText);
        Assert.Equal("Calidad baja (flujo secundario): más fluida con mala conexión. Pulsa para calidad alta.", tile.QualityToolTip);
        tile.SetLowQuality(false);
        Assert.Equal("HD", tile.QualityText);
        Assert.Equal("Calidad alta (flujo principal). Pulsa para calidad baja si la imagen va a saltos.", tile.QualityToolTip);
    }

    [Fact]
    public void Quality_button_only_on_big_manage_tiles()
    {
        var tile = NewTile();
        Assert.False(tile.QualityButtonVisible);
        tile.Big = true;
        Assert.True(tile.QualityButtonVisible);
        var fullscreen = NewTile(manage: false);
        fullscreen.Big = true;
        Assert.False(fullscreen.QualityButtonVisible);
    }

    [Fact]
    public void Recording_off()
    {
        var tile = NewTile();
        tile.SetRecordingStatus(RecordingStatus.Recording);
        tile.SetRecordingStatus(RecordingStatus.Off);
        Assert.False(tile.RecDotVisible);
        Assert.Equal("⏺", tile.RecordText);
        Assert.Equal("Grabar", tile.RecordToolTip);
    }

    [Theory]
    [InlineData(RecordingStatus.Recording, false, "Grabando")]
    [InlineData(RecordingStatus.Paused, true, "Grabación en pausa: reconectando…")]
    [InlineData(RecordingStatus.Connecting, true, "Grabación: conectando…")]
    public void Recording_dot_and_button(RecordingStatus status, bool hollow, string dotToolTip)
    {
        var tile = NewTile();
        tile.SetRecordingStatus(status);
        Assert.Equal(status, tile.Recording);
        Assert.True(tile.RecDotVisible);
        Assert.Equal(hollow, tile.RecDotHollow);
        Assert.Equal(dotToolTip, tile.RecDotToolTip);
        Assert.Equal("⏹", tile.RecordText);
        Assert.Equal("Detener grabación", tile.RecordToolTip);
    }

    [Fact]
    public void Motion_tooltips()
    {
        var tile = NewTile();
        tile.SetMotionEnabled(false);
        Assert.False(tile.MotionEnabled);
        Assert.Equal("Detección de movimiento: desactivada", tile.MotionToolTip);
        tile.SetMotionEnabled(true);
        Assert.True(tile.MotionEnabled);
        Assert.Equal("Detección de movimiento: activada", tile.MotionToolTip);
    }

    [Fact]
    public void Smoothing_check_follows_the_setting()
    {
        var tile = NewTile();
        tile.SetSmoothing(true);
        Assert.True(tile.SmoothingOn);
        tile.SetSmoothing(false);
        Assert.False(tile.SmoothingOn);
    }

    [Fact]
    public void Audio_button_needs_a_capable_tile_and_an_audio_track()
    {
        var tile = NewTile();
        Assert.False(tile.AudioButtonVisible);
        tile.AudioCapable = true;
        Assert.False(tile.AudioButtonVisible);
        tile.HasAudio = true;
        Assert.True(tile.AudioButtonVisible);
        tile.AudioCapable = false;
        Assert.False(tile.AudioButtonVisible);
    }

    [Fact]
    public void Audio_texts()
    {
        var tile = NewTile();
        Assert.Equal("🔇", tile.AudioText);
        Assert.Equal("Activar sonido", tile.AudioToolTip);
        tile.ApplyAudioOn(true);
        Assert.True(tile.AudioOn);
        Assert.Equal("🔊", tile.AudioText);
        Assert.Equal("Silenciar", tile.AudioToolTip);
        tile.ApplyAudioOn(false);
        Assert.False(tile.AudioOn);
        Assert.Equal("🔇", tile.AudioText);
        Assert.Equal("Activar sonido", tile.AudioToolTip);
    }

    [Fact]
    public void Disabled_audio_keeps_the_reason_as_tooltip()
    {
        var tile = NewTile();
        tile.DisableAudio("No hay salida de audio");
        Assert.False(tile.AudioEnabled);
        Assert.Equal("No hay salida de audio", tile.AudioToolTip);
        tile.ApplyAudioOn(true);
        Assert.Equal("No hay salida de audio", tile.AudioToolTip);
    }

    [Fact]
    public void Fix_button_after_auth_failure_until_playing()
    {
        var tile = NewTile();
        Assert.False(tile.ShowFixButton);
        tile.ShowState(SessionState.AuthFailed);
        Assert.True(tile.ShowFixButton);
        tile.ShowState(SessionState.Playing);
        Assert.False(tile.ShowFixButton);
    }

    [Fact]
    public void A_shared_session_that_already_failed_auth_shows_the_fix_button()
    {
        var tile = NewTile();
        tile.ShowLastError(StreamErrorKind.AuthFailed);
        Assert.True(tile.ShowFixButton);
        tile.ShowState(SessionState.Reconnecting);
        Assert.True(tile.ShowFixButton);
        Assert.Equal(TileStatus.Text(SessionState.Reconnecting, StreamErrorKind.AuthFailed, tile.Camera), tile.StatusText);
    }

    [Fact]
    public void A_shared_session_that_failed_otherwise_shows_no_fix_button()
    {
        var tile = NewTile();
        tile.ShowLastError(StreamErrorKind.Unreachable);
        Assert.False(tile.ShowFixButton);
        var fullscreen = NewTile(manage: false);
        fullscreen.ShowLastError(StreamErrorKind.AuthFailed);
        Assert.False(fullscreen.ShowFixButton);
    }

    [Fact]
    public void No_fix_button_on_fullscreen_tiles()
    {
        var tile = NewTile(manage: false);
        tile.ShowState(SessionState.AuthFailed);
        Assert.False(tile.ShowFixButton);
    }

    [Fact]
    public void Without_a_session_there_is_nothing_to_capture_or_record()
    {
        var tile = NewTile();
        Assert.False(tile.HasSession);
        Assert.False(tile.CanSnapshot);
    }
}
