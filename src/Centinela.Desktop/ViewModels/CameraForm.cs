using Centinela.Core;

namespace Centinela.Desktop.ViewModels;

/// <summary>What the «Añadir / Editar cámara» form shows, as typed (not trimmed).</summary>
sealed record CameraFields(
    Brand Brand, string Name, string Host, string Port, string User, string Password,
    string MainUrl, string SubUrl, bool UseUdp);

/// <summary>
/// The camera dialog's rules: validates the form and applies it to a copy of the camera (<see cref="Result"/>), so
/// the stored one only changes when the window saves it. Same rules and texts as the Windows dialog.
/// </summary>
sealed class CameraForm(Camera camera)
{
    readonly Camera _camera = camera.Clone();

    /// <summary>The copy the form edits; a new camera or an edited copy with the same Id.</summary>
    public Camera Result => _camera;

    /// <summary>Host the override URLs currently point at; moved along when the user edits the IP or the port.</summary>
    public string UrlHost { get; private set; } = camera.Host;

    /// <summary>Port the override URLs currently point at.</summary>
    public int UrlPort { get; private set; } = camera.Port;

    /// <summary>Shown when the stored password cannot be read on this system (<see cref="Camera.PasswordMissing"/>).</summary>
    public const string PasswordNote = "La contraseña guardada no se puede leer en este equipo: escríbela de nuevo.";

    public static CameraFields FieldsOf(Camera camera) => new(
        camera.Brand, camera.Name, camera.Host, camera.Port.ToString(), camera.User, camera.Password,
        camera.MainUrlOverride ?? "", camera.SubUrlOverride ?? "", camera.UseUdp);

    public static string HintFor(Brand brand) => brand switch
    {
        Brand.Tapo => "Usa el usuario y la contraseña de la «Cuenta de cámara» (app Tapo › Ajustes avanzados).",
        Brand.Imou => "Usuario «admin». La contraseña es el código de seguridad de la pegatina. Si no conecta, activa RTSP/ONVIF en la app Imou.",
        _ => "Pon la URL RTSP en «Avanzado» sin usuario ni contraseña (rtsp://IP:puerto/ruta). Escribe las credenciales en los campos Usuario y Contraseña: se guardan cifradas.",
    };

    /// <summary>
    /// Validates the fields and, when valid, fills <see cref="Result"/>. <paramref name="mainUrl"/> and
    /// <paramref name="subUrl"/> are the «Avanzado» URLs to show again: moved to the new IP/port when those changed.
    /// </summary>
    public bool TryApply(CameraFields fields, out string error, out string mainUrl, out string subUrl)
    {
        mainUrl = fields.MainUrl.Trim();
        subUrl = fields.SubUrl.Trim();
        error = "";
        var port = 0;
        if (fields.Name.Trim().Length == 0) error = "Pon un nombre a la cámara.";
        else if (fields.Brand != Brand.Custom && fields.Host.Trim().Length == 0) error = "La IP es obligatoria.";
        else if (fields.Brand == Brand.Custom && mainUrl.Length == 0) error = "La URL principal es obligatoria.";
        else if (!int.TryParse(fields.Port, out port) || port is < 1 or > 65535) error = "Puerto no válido.";
        if (error.Length > 0) return false;

        var host = fields.Host.Trim();
        if (!string.Equals(host, UrlHost, StringComparison.OrdinalIgnoreCase) || port != UrlPort)
        {
            // ONVIF-added cameras keep their stream URLs as overrides: follow the IP/port change.
            mainUrl = StreamUrlBuilder.RebaseOverride(mainUrl, UrlHost, UrlPort, host, port);
            subUrl = StreamUrlBuilder.RebaseOverride(subUrl, UrlHost, UrlPort, host, port);
            UrlHost = host;
            UrlPort = port;
        }

        _camera.Brand = fields.Brand;
        _camera.Name = fields.Name.Trim();
        _camera.Host = host;
        _camera.Port = port;
        _camera.User = fields.User.Trim();
        _camera.Password = fields.Password;
        if (fields.Password.Length > 0) _camera.UnreadablePassword = null;
        _camera.MainUrlOverride = mainUrl.Length == 0 ? null : mainUrl;
        _camera.SubUrlOverride = subUrl.Length == 0 ? null : subUrl;
        _camera.UseUdp = fields.UseUdp;
        return true;
    }
}
