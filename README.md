# CamaraWin

Visor de cámaras IP para Windows con **latencia mínima**. Muestra cámaras **TP-Link Tapo**,
**Imou** y cualquier cámara **RTSP** en cuadrícula, sin pasar por la nube.

- Objetivo (no medido): imagen en menos de 1 s y ~100–300 ms de retardo en red local (FFmpeg sin búfer + GPU).
- Añadir cámaras a mano o **buscarlas en la red** (ONVIF).
- Vistas: cuadrícula (Auto, 1, 4, 9, 16) o **Principal + miniaturas** (clic en una miniatura para cambiar la principal).
- Doble clic: pantalla completa en calidad alta. Arrastrar: reordenar. F11: ventana a pantalla completa.
- 📷 Capturas PNG a resolución completa · ⏺ Grabación MKV sin recodificar · **⏺ Grabar todas** / ⏹ Detener todas.
- Errores traducidos al español («Contraseña incorrecta», «Sin conexión · reintentando»…) con avisos emergentes
  y un **Registro** de errores (botón «Registro»; archivos en `%AppData%\CamaraWin\logs`, se borran a los 14 días).
- Menú **Archivo**: importar/exportar la lista de cámaras en JSON, con las contraseñas cifradas con una clave
  opcional (sin clave se exporta sin contraseñas). Al importar se añaden las cámaras nuevas y se actualizan
  las que ya existen (misma IP y puerto). Formato: [`docs/ejemplo-camaras.json`](docs/ejemplo-camaras.json).
- Copia automática sin contraseñas en `Documentos\CamaraWin\camaras-copia.json` cada vez que cambia la lista
  (carpeta configurable en Archivo › Carpeta de copia automática…).
- **Bandeja del sistema**: la X oculta la ventana y las grabaciones continúan; «Salir» la cierra del todo.
  Una sola instancia: abrirla otra vez muestra la ventana existente. Menú de la bandeja con
  **Arrancar con Windows** (se inicia oculta en la bandeja con el argumento `--tray`).
- Ver › **Mostrar estadísticas**: `fps · ms · GPU/CPU` en cada cámara. Los ms son lo que tarda el equipo en
  preparar cada imagen, **no** el retardo real cámara → pantalla (no se puede medir).
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
El proyecto de la aplicación (`src/CamaraWin.App`) no tiene pruebas automáticas. Para probarla a mano
(smoke) sin cámaras reales también hacen falta mediamtx y ffmpeg con libx264; publica un vídeo de prueba con
`-pix_fmt yuv420p`. Los dos comandos se quedan en marcha, así que usa **dos terminales**:

```powershell
# Terminal 1: servidor RTSP
tools/bin/mediamtx.exe
```

```powershell
# Terminal 2: vídeo de prueba
ffmpeg -re -f lavfi -i testsrc=size=1280x720:rate=25 -c:v libx264 -pix_fmt yuv420p -tune zerolatency -f rtsp rtsp://127.0.0.1:8554/prueba
```

Después, en la aplicación, añade una cámara de marca **Otra (RTSP)** con la URL principal
`rtsp://127.0.0.1:8554/prueba` (en «Avanzado»).

### CAMARAWIN_DATA_DIR (pruebas sin tocar tu instalación)

Para no tocar tus cámaras reales, define la variable de entorno `CAMARAWIN_DATA_DIR` con otra carpeta
(en una tercera terminal):

```powershell
$env:CAMARAWIN_DATA_DIR = "$env:TEMP\camarawin-prueba"
dotnet run --project src/CamaraWin.App            # añade "-- --tray" para arrancar oculta en la bandeja
```

Con la variable definida, todo lo que escribe la aplicación queda dentro de esa carpeta:

- cámaras, ajustes y registro (en lugar de `%AppData%\CamaraWin`);
- `backup\` para la copia automática (en lugar de `Documentos\CamaraWin`);
- `recordings\` para las grabaciones (en lugar de `Vídeos\CamaraWin`);
- `snapshots\` para las capturas (en lugar de `Imágenes\CamaraWin`).

Además es una instancia aparte: no despierta ni se confunde con la CamaraWin que tengas abierta, y
«Arrancar con Windows» no aparece en el menú de la bandeja (no se toca el registro de Windows).

## Datos

- Cámaras: `%AppData%\CamaraWin\cameras.json` (contraseñas cifradas, solo tu usuario de Windows puede leerlas)
- Registro de errores: `%AppData%\CamaraWin\logs\`
- Copia automática: `Documentos\CamaraWin\camaras-copia.json` (o la carpeta elegida)
- Grabaciones: `Vídeos\CamaraWin\<cámara>\`
- Capturas: `Imágenes\CamaraWin\`

Con `CAMARAWIN_DATA_DIR` todas estas rutas pasan a esa carpeta (ver
[CAMARAWIN_DATA_DIR](#camarawin_data_dir-pruebas-sin-tocar-tu-instalación)).

## Soporte

https://www.tecxart.es · [tecxart@gmail.com](mailto:tecxart@gmail.com) · también en la aplicación: Archivo › Soporte.

## Licencia

MIT. Ver [LICENSE](LICENSE) y [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) (FFmpeg es LGPL).
