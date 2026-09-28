# Centinela

Visor de cámaras IP para Windows con **latencia mínima**. Muestra cámaras **TP-Link Tapo**,
**Imou** y cualquier cámara **RTSP** en cuadrícula, sin pasar por la nube.

- Objetivo (no medido): imagen en menos de 1 s y ~100–300 ms de retardo en red local (FFmpeg sin búfer + GPU).
- Añadir cámaras a mano o **buscarlas en la red** (ONVIF).
- Vistas: cuadrícula (Auto, 1, 4, 9, 16), **Principal + miniaturas** a la derecha o a la izquierda (clic en una
  miniatura para cambiar la principal) o **Dos principales + miniaturas**: clic en una miniatura → sustituye a la
  grande que lleva más tiempo sin cambiar; arrastrarla sobre una grande → la pone ahí. Se recuerda la elección.
- Doble clic: pantalla completa en calidad alta. Arrastrar: reordenar. F11: ventana a pantalla completa.
- **Clic derecho** en una cámara: Pantalla completa, Captura, Grabar/Detener grabación, Editar…, **Duplicar…**
  (copia con «(copia)» en el nombre), Restablecer zoom y Eliminar.
- **Probar** (al añadir o editar una cámara): prueba el vídeo principal y el secundario (hasta 10 s) y muestra
  resolución, códec y audio, p. ej. «Principal: 2560×1440 · H.265 · audio AAC», o el error traducido.
- **Zoom digital** en las cámaras grandes y a pantalla completa: rueda del ratón (1×–8×, hacia el cursor),
  arrastrar para moverse (sin reordenar mientras hay zoom) y `0` (con el puntero sobre la cámara) o el menú
  para volver a 1×. No se guarda.
- **Calidad HD/SD** en las cámaras grandes: el botón HD/SD (abajo en el centro, al pasar el ratón) cambia la vista
  grande de esa cámara entre el flujo principal (HD) y el secundario (SD), más ligero y fluido si la conexión
  (p. ej. wifi lejana) no da para el principal. Se guarda por cámara y también vale a pantalla completa; en SD
  no hay sonido en la vista grande. Las capturas siguen tomándose del principal.
- **Suavizado automático**: si la conexión de una cámara va a golpes (dos parones de más de 0,3 s en 30 s, típico
  de una wifi lejana), Centinela pasa esa cámara a reproducir con un colchón de 0,5–2 s: las imágenes salen a su
  ritmo en vez de a trompicones y sin «avance rápido» tras un parón. Se ve un ⏱ junto al nombre (y «suav. N s»
  en las estadísticas); el sonido va con el mismo retraso. Dura hasta cerrar la cámara o Centinela; las
  cámaras con buena conexión no se tocan (sin retraso). Las grabaciones no cambian.
- **Sonido**: botón 🔇/🔊 solo en las cámaras grandes y a pantalla completa, si la cámara envía audio. Todas empiezan
  en silencio y solo suena una a la vez; se calla al ocultar en la bandeja o cerrar la pantalla completa.
  El volumen, en el mezclador de Windows.
- 📷 Capturas PNG a resolución completa · ⏺ Grabación MKV sin recodificar · **⏺ Grabar todas** / ⏹ Detener todas.
- Errores traducidos al español («Contraseña incorrecta», «Sin conexión · reintentando»…) con avisos emergentes
  y un **Registro** de errores (botón «Registro»; archivos en `%AppData%\Centinela\logs`, se borran a los 14 días).
- **Detección de movimiento** por cámara (desactivada de serie): botón 👁 de la cámara, clic derecho ›
  «Detección de movimiento» o Ver › Opciones de avisos…. Un 👁 tachado junto al nombre indica que está apagada.
  Sigue funcionando con la ventana en la bandeja. Mientras hay movimiento la cámara lleva un **marco rojo**
  (miniaturas, grandes y pantalla completa) que se quita 3 s después de que pare.
- Aviso junto al reloj «Detección de movimiento: "Nombre"» **solo** con la ventana minimizada o en la bandeja
  (clic → abre la ventana). Como mucho un aviso por cámara cada tiempo de espera (30 s, 1 min —de serie—, 5 o
  15 min). Sensibilidad Baja, Media o Alta.
- Botón **Movimiento** en la barra de estado (con el número de eventos sin ver): registro con inicio, cámara,
  duración y cambio máximo; Copiar, Abrir carpeta y Vaciar. Cada evento se anota al terminar (o al salir).
- Consumo: cada cámara vigilada usa una conexión en calidad baja (la misma de la miniatura si está visible).
- Ver › **Opciones de avisos…**: tabla por cámara (Detección, Sensibilidad, Espera, Avisos de conexión, Avisos de
  movimiento), sonidos de Windows (al perder la conexión; con movimiento, solo cuando sale el aviso) y
  **horas de silencio** HH:mm–HH:mm (pueden cruzar la medianoche): sin avisos ni sonidos, pero el registro y el
  marco rojo siguen. Los avisos de conexión también respetan «Avisos de conexión». Las opciones de cada cámara
  van en `cameras.json` y en la copia JSON (las copias antiguas se importan con la detección apagada); los
  sonidos y las horas de silencio se guardan en `settings.json` y no van en la copia.
- Menú **Archivo**: importar/exportar la lista de cámaras en JSON, con las contraseñas cifradas con una clave
  opcional (sin clave se exporta sin contraseñas). Al importar se añaden las cámaras nuevas y se actualizan
  las que ya existen (misma IP y puerto). Formato: [`docs/ejemplo-camaras.json`](docs/ejemplo-camaras.json).
- Copia automática sin contraseñas en `Documentos\Centinela\camaras-copia.json` cada vez que cambia la lista
  (carpeta configurable en Archivo › Carpeta de copia automática…).
- **Bandeja del sistema**: la X oculta la ventana y las grabaciones continúan; «Salir» la cierra del todo.
  Una sola instancia: abrirla otra vez muestra la ventana existente. Menú de la bandeja con
  **Arrancar con Windows** (se inicia oculta en la bandeja con el argumento `--tray`).
- Ver › **Mostrar estadísticas**: `fps · ms · GPU/CPU` en cada cámara. Los ms son lo que tarda el equipo en
  preparar cada imagen, **no** el retardo real cámara → pantalla (no se puede medir).
- Menú **Ayuda**: Manual, Licencia, Soporte, **Buscar actualizaciones…** y Acerca de Centinela.
- Actualizaciones: una vez al día, al arrancar, consulta la última versión publicada en GitHub
  (`tecxion/centinela`) y avisa con «Ver»; se puede omitir una versión o desactivar con «Comprobar al arrancar».
  **Nunca descarga nada sola**: «Descargar» abre la página de GitHub. A `api.github.com` solo se envía la versión
  de la aplicación (en el `User-Agent`). Con `CENTINELA_DATA_DIR` no se comprueba al arrancar (sí desde el menú).
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
dotnet run --project src/Centinela.App
```

Pruebas:

```powershell
dotnet test tests/Centinela.Core.Tests
pwsh -File tools/get-mediamtx.ps1    # servidor RTSP para las pruebas de vídeo
dotnet test tests/Centinela.Media.Tests   # necesita ffmpeg/ffprobe (con libx264) en el PATH
```

Las pruebas de integración de vídeo de `Centinela.Media.Tests` necesitan `tools/bin/mediamtx.exe`
**y** `ffmpeg`/`ffprobe` (compilados con libx264) en el PATH.
El proyecto de la aplicación (`src/Centinela.App`) no tiene pruebas automáticas. Para probarla a mano
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

### CENTINELA_DATA_DIR (pruebas sin tocar tu instalación)

Para no tocar tus cámaras reales, define la variable de entorno `CENTINELA_DATA_DIR` con otra carpeta
(en una tercera terminal):

```powershell
$env:CENTINELA_DATA_DIR = "$env:TEMP\centinela-prueba"
dotnet run --project src/Centinela.App            # añade "-- --tray" para arrancar oculta en la bandeja
```

Con la variable definida, todo lo que escribe la aplicación queda dentro de esa carpeta:

- cámaras, ajustes y registros de errores y de movimiento (en lugar de `%AppData%\Centinela`);
- `backup\` para la copia automática (en lugar de `Documentos\Centinela`);
- `recordings\` para las grabaciones (en lugar de `Vídeos\Centinela`);
- `snapshots\` para las capturas (en lugar de `Imágenes\Centinela`).

Además es una instancia aparte: no despierta ni se confunde con la Centinela que tengas abierta, y
«Arrancar con Windows» no aparece en el menú de la bandeja (no se toca el registro de Windows).

## Publicar una versión

1. Cambia `<Version>` en `Directory.Build.props` (p. ej. `1.3.0`).
2. Crea y **publica** en GitHub (`tecxion/centinela`) una *release* con la etiqueta `vX.Y.Z` o `vX.Y`
   (2 o 3 números, p. ej. `v1.3.0`). Los borradores y las *pre-releases* no cuentan: la comprobación usa
   la última *release* publicada.

Las copias instaladas la verán en su siguiente comprobación (o con Ayuda › Buscar actualizaciones…).

## Datos

- Cámaras y sus opciones de avisos: `%AppData%\Centinela\cameras.json` (contraseñas cifradas, solo tu usuario de Windows puede leerlas)
- Ajustes, sonidos y horas de silencio: `%AppData%\Centinela\settings.json`
- Registro de errores: `%AppData%\Centinela\logs\centinela-AAAA-MM-DD.log`
- Registro de movimiento: `%AppData%\Centinela\logs\movimiento-AAAA-MM-DD.log` (los registros se borran a los 14 días)
- Copia automática: `Documentos\Centinela\camaras-copia.json` (o la carpeta elegida)
- Grabaciones: `Vídeos\Centinela\<cámara>\`
- Capturas: `Imágenes\Centinela\`

Con `CENTINELA_DATA_DIR` todas estas rutas pasan a esa carpeta (ver
[CENTINELA_DATA_DIR](#centinela_data_dir-pruebas-sin-tocar-tu-instalación)).

### Si venías de CamaraWin

Centinela antes se llamaba CamaraWin. Al arrancar por primera vez copia `cameras.json` y `settings.json`
de `%AppData%\CamaraWin` (la carpeta antigua no se toca), cambia la entrada «Arrancar con Windows» por la
nueva e importa sin problema las copias JSON hechas con CamaraWin. Las grabaciones y capturas antiguas
siguen en `Vídeos\CamaraWin` e `Imágenes\CamaraWin`.

## Icono

`src/Centinela.App/Assets/centinela.png` es el original. Tras cambiarlo, regenera el `.ico`
(16–256 px) con `pwsh tools/make-icon.ps1`.

## Soporte

https://www.tecxart.es · [tecxart@gmail.com](mailto:tecxart@gmail.com) · también en la aplicación: Ayuda › Soporte.

## Licencia

MIT. Ver [LICENSE](LICENSE) y [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) (FFmpeg es LGPL).
