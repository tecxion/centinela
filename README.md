# CamaraWin

Visor de cámaras IP para Windows con **latencia mínima**. Muestra cámaras **TP-Link Tapo**,
**Imou** y cualquier cámara **RTSP** en cuadrícula, sin pasar por la nube.

- Imagen en menos de 1 s y ~100–300 ms de retardo en red local (FFmpeg sin búfer + GPU).
- Añadir cámaras a mano o **buscarlas en la red** (ONVIF).
- Doble clic: pantalla completa en calidad alta. Arrastrar: reordenar.
- 📷 Capturas PNG a resolución completa · ⏺ Grabación MKV sin recodificar.
- Contraseñas cifradas con DPAPI de Windows.

## Preparar las cámaras

| Marca | Qué hacer | URL que usa la app |
|---|---|---|
| Tapo | App Tapo › cámara › Ajustes avanzados › **Cuenta de cámara** (usuario/contraseña) | `rtsp://IP:554/stream1` (alta) · `/stream2` (baja) |
| Imou | Usuario `admin`, contraseña = **código de seguridad** de la pegatina. En algunos modelos hay que activar RTSP/ONVIF en la app Imou | `rtsp://IP:554/cam/realmonitor?channel=1&subtype=0` · `subtype=1` |

## Compilar

Requisitos: Windows 10/11, .NET SDK 10.

```powershell
pwsh -File tools/get-ffmpeg.ps1      # descarga FFmpeg 9.0 LGPL (≈ 80 MB)
dotnet run --project src/CamaraWin.App
```

Pruebas:

```powershell
dotnet test tests/CamaraWin.Core.Tests
pwsh -File tools/get-mediamtx.ps1    # servidor RTSP para las pruebas de vídeo
dotnet test tests/CamaraWin.Media.Tests   # necesita ffmpeg/ffprobe (con libx264) en el PATH
```

Las pruebas de integración de vídeo de `CamaraWin.Media.Tests` necesitan `tools/bin/mediamtx.exe`
**y** `ffmpeg`/`ffprobe` (compilados con libx264) en el PATH.
El proyecto de la aplicación (`src/CamaraWin.App`) no tiene pruebas automáticas.

## Datos

- Cámaras: `%AppData%\CamaraWin\cameras.json` (contraseñas cifradas, solo tu usuario de Windows puede leerlas)
- Grabaciones: `Vídeos\CamaraWin\<cámara>\`
- Capturas: `Imágenes\CamaraWin\`

## Licencia

MIT. Ver [LICENSE](LICENSE) y [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) (FFmpeg es LGPL).
