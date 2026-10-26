# Centinela v1.4 — prueba en macOS

Recórrela en el MacBook (Apple Silicon, macOS 14 o posterior) con el `.dmg` de prueba que crea GitHub. Es la misma
idea que `docs/prueba-linux-v1.4.md`, cambiada a lo que se ve en macOS. Este `.dmg` no lleva todavía la firma de
Apple (llega en la fase 5), así que macOS pide algún permiso más que con la versión final.

Cómo usarla: haz lo que dice cada punto y comprueba que ves lo que se indica. Si algo no sale igual, apunta el número
de la sección, lo que ha pasado y, si sale, el texto exacto del mensaje. Las órdenes `así` se escriben en la app
Terminal (⌘ + Espacio y escribe «Terminal») y se confirman con Intro.

## 1. Descargar

- [ ] En Windows, sube la rama: `git push -u origin feat/v1.4`. Cada subida que cambia algo más que documentos crea
      un `.dmg` nuevo en GitHub. La primera vez tarda más, porque compila FFmpeg; después lo guarda y lo reutiliza.
- [ ] Inicia sesión en GitHub (sin sesión no se pueden descargar los archivos). Entra en
      https://github.com/tecxion/centinela, pestaña «Actions», y a la izquierda elige «macOS (.dmg de prueba)».
- [ ] Abre la última ejecución de `feat/v1.4` con la marca verde. Abajo, en «Artifacts», descarga
      `Centinela-macos-arm64`.
- [ ] Se descarga un `.zip`. Ábrelo con doble clic en Finder: dentro está `Centinela-1.3.0-macos-arm64.dmg` (pone
      1.3.0 hasta que se cambie la versión al final).

## 2. Instalar y primer arranque

- [ ] Doble clic en el `.dmg`. Se abre una ventana con Centinela y la carpeta Aplicaciones: arrastra Centinela
      encima de Aplicaciones. Si ya había uno, sal antes de Centinela con ⌘Q (o «Salir» en el menú del icono de la
      barra de menús) y elige «Reemplazar».
      Después expulsa el disco «Centinela» (botón ⏏ en la barra lateral de Finder).
- [ ] Abre Centinela desde Aplicaciones. La primera vez macOS no lo deja abrir porque no está notarizado (el aviso
      dice que Apple no ha podido comprobarlo). Cierra el aviso sin elegir «Trasladar a la Papelera». Ve a Ajustes
      del Sistema › Privacidad y seguridad, baja hasta «Seguridad» y pulsa «Abrir igualmente» junto a Centinela;
      confirma con tu contraseña o Touch ID.
- [ ] Si no sale «Abrir igualmente», en Terminal: `xattr -dr com.apple.quarantine /Applications/Centinela.app` y
      ábrelo otra vez.
- [ ] Al abrirlo, macOS pregunta si Centinela puede enviar notificaciones: «Permitir».
- [ ] Al conectar con las cámaras o al buscar en red, macOS pregunta si Centinela puede buscar dispositivos de la red
      local y conectarse a ellos (con el texto «Centinela se conecta a las cámaras de tu red local y las busca en
      ella.»): «Permitir».
- [ ] Al hacerse la copia automática de la lista de cámaras (por ejemplo, al guardar una cámara), macOS pregunta si
      Centinela puede acceder a la carpeta Documentos (con el texto «Centinela guarda en Documentos la copia
      automática de la lista de cámaras.»): «Permitir».
- [ ] Llavero: Centinela guarda las contraseñas de las cámaras en el Llavero de macOS. Tras instalar cada `.dmg`
      nuevo, al abrir Centinela macOS pregunta si puede usar la contraseña guardada, una ventana por cámara. Escribe
      la contraseña de tu Mac y pulsa «Permitir siempre». Mientras esa ventana está abierta, la ventana de Centinela
      espera (no sale o no responde): es normal. Pasa por la firma provisional de estos `.dmg` de prueba; con la firma
      de la versión final (fase 5) dejará de salir tras cada actualización. Apunta si sale también sin haber
      instalado un `.dmg` nuevo.
- [ ] Como con el Llavero, tras instalar cada `.dmg` de prueba nuevo macOS puede volver a preguntar por la red local y
      por la carpeta Documentos: también es por la firma provisional, no es un fallo. «Permitir» otra vez.

## 3. Comprobar

### Cámaras, vídeo y sonido

- [ ] Añade una cámara con «➕ Añadir cámara» y pulsa «Probar»: dice la resolución, el códec y si tiene audio. Guarda:
      la cámara se ve.
- [ ] Ver › Mostrar estadísticas: cada cámara dice `GPU` (el vídeo lo descodifica VideoToolbox) y `pintado GPU` (lo
      pinta OpenGL). Si alguna dice `CPU`, apunta cuál y su códec.
- [ ] En una cámara con audio pulsa 🔇: se oye la cámara y el botón pasa a 🔊. El volumen se cambia con el del Mac.
- [ ] Ver › Opciones de avisos… con los sonidos activados: se oyen los dos tonos al perder una cámara y los dos
      pitidos con movimiento.

### Barra de menús, Dock y menú de la aplicación

- [ ] Arriba a la derecha, en la barra de menús, sale el icono de Centinela. Su menú tiene «Abrir», «Grabar todas»,
      «Abrir al iniciar sesión» y «Salir»; «Abrir» muestra la ventana. En un MacBook con muesca, si hay muchos iconos
      el de Centinela puede quedar escondido detrás de la muesca: cierra otras apps de la barra de menús, o mantén
      pulsada ⌘ y arrastra iconos para hacerle sitio.
- [ ] El botón rojo de la ventana la oculta (Centinela sigue abierto y las grabaciones siguen). La primera vez sale una
      notificación de macOS que lo explica; tiene que verse aunque Centinela esté delante.
- [ ] Con la ventana oculta, un clic en el icono de Centinela del Dock la vuelve a abrir.
- [ ] En el menú de la aplicación (el menú «Centinela», junto a la manzana) sale «Acerca de Centinela», no «About
      Avalonia», y abre la ventana «Acerca de».
- [ ] Con esa ventana abierta, elige otra vez «Acerca de Centinela» en el menú: apunta si se abre una segunda ventana.
- [ ] ⌘Q cierra Centinela del todo: la ventana y el icono de la barra de menús desaparecen.

### Abrir al iniciar sesión

Antes de cada «Cierra la sesión» de esta sección, sal de Centinela con ⌘Q y, en la ventana que pregunta si quieres
cerrar la sesión, desmarca «Volver a abrir las ventanas al volver a iniciar sesión». Si al entrar se abre sola la
ventana de Centinela, viene de esa opción de macOS, no de Centinela.

- [ ] Marca «Abrir al iniciar sesión» en el menú del icono. macOS avisa «Ítem añadido en segundo plano». Comprueba el
      archivo: `cat ~/Library/LaunchAgents/es.tecxart.centinela.plist`. Tiene `/Applications/Centinela.app/Contents/MacOS/Centinela`
      y `--tray`.
- [ ] Sal con ⌘Q. Cierra la sesión (menú de la manzana › Cerrar sesión) y vuelve a entrar. Centinela arranca solo,
      oculto en la barra de menús, y las cámaras vigiladas siguen vigilándose.
- [ ] En Ajustes del Sistema › General › Ítems de inicio (en macOS 15 se llama «Ítems de inicio y extensiones»), en
      «Permitir en segundo plano», apaga el interruptor de Centinela. Ojo: macOS no guarda ese interruptor en el
      archivo de arriba, así que la casilla de Centinela puede seguir marcada aunque esté apagado. Sal con ⌘Q. Cierra
      la sesión y entra: apunta si Centinela arranca (con el interruptor apagado no debería). Vuelve a encenderlo.
- [ ] Desmarca «Abrir al iniciar sesión» en Centinela: el archivo desaparece (`ls ~/Library/LaunchAgents`). Sal con
      ⌘Q. Cierra la sesión y entra: Centinela ya no arranca solo.

### Una sola copia abierta

- [ ] Con Centinela abierto, ábrelo otra vez desde Aplicaciones: no se abre otro, se muestra la ventana que ya estaba
      (también si estaba oculta).
- [ ] Lo mismo forzando una segunda copia, en Terminal: `open -n /Applications/Centinela.app`. La segunda copia se
      cierra sola enseguida y se muestra la ventana de la primera; sigue habiendo un solo icono en la barra de menús.

### Movimiento y notificaciones

- [ ] Activa 👁 en una cámara. Con la ventana minimizada (botón amarillo) u oculta (botón rojo), al haber movimiento
      sale una notificación de macOS «Detección de movimiento: «Nombre»». Un clic en la notificación abre la ventana.
- [ ] Si no sale ninguna: Ajustes del Sistema › Notificaciones › Centinela, «Permitir notificaciones» activado (y el
      modo Concentración apagado).
- [ ] Las notificaciones solo funcionan con Centinela abierto desde el `.app`. Abierto de otra forma (por ejemplo con
      `dotnet run` desde el código) no salen: es así a propósito, no es un fallo.

### Grabaciones, capturas y carpetas

- [ ] 📷 en una cámara: la captura PNG está en Imágenes/Centinela. Un clic en la línea «Captura guardada: …» de abajo
      la muestra en Finder.
- [ ] Graba una cámara: los archivos MKV están en Películas/Centinela, en una carpeta por cámara. «📁 Grabaciones»
      abre esa carpeta en Finder.
- [ ] App Nap: graba una cámara al menos 15 minutos con la ventana oculta en la barra de menús (botón rojo). En
      Monitor de Actividad, la columna «App Nap» de Centinela dice «No» (si no está, añádela desde el menú de las
      columnas: clic derecho en la cabecera de la tabla). La grabación no tiene cortes.

### Red

- [ ] «🔍 Buscar en red» encuentra las cámaras de tu red (multidifusión).
- [ ] «📡 Detectar otras redes» termina sin decir «No se pudo hacer ping en este sistema…».
- [ ] Sal de Centinela. En Ajustes del Sistema › Privacidad y seguridad › Red local, apaga Centinela y ábrelo: las
      cámaras dicen «Sin conexión» y su consejo pide permitir a Centinela el acceso a la red local en Ajustes del
      Sistema › Privacidad y seguridad › Red local. Vuelve a encenderlo, sal y abre Centinela: se ven.

### Copias de seguridad

- [ ] Archivo › Exportar JSON… sin contraseñas y con contraseñas (clave de al menos 8 caracteres, dos veces).
- [ ] Archivo › Importar JSON…: con la copia con contraseñas de Windows, pide la clave; con la clave correcta las
      cámaras se ven con su contraseña.
- [ ] Cambia algo de una cámara: `Documentos/Centinela/camaras-copia.json` se actualiza solo.

### Actualizaciones

- [ ] Ayuda › Buscar actualizaciones…: si hay una versión nueva, la línea «Archivo para este equipo» dice
      `Centinela-…-macos-arm64.dmg`.

### Pintado con el procesador

- [ ] Sal de Centinela y ábrelo desde Terminal con el pintado antiguo:
      `CENTINELA_CPU_VIDEO=1 /Applications/Centinela.app/Contents/MacOS/Centinela`. Las estadísticas dicen
      `pintado CPU`. Con las mismas cámaras, compara en Monitor de Actividad el % de CPU de Centinela con el de
      `pintado GPU` (debería ser mayor). Ciérralo con ⌘Q.

## 4. Opcional: Intel en Rosetta

- [ ] En la misma ejecución de GitHub descarga también `Centinela-macos-x64` (dentro va `…-macos-intel.dmg`). Sal de
      Centinela e instálalo como en la sección 2, reemplazando el de Aplicaciones. Si macOS pide instalar Rosetta,
      acepta.
- [ ] Funciona igual, pero más lento. En Monitor de Actividad, la columna «Tipo» de Centinela dice «Intel».
- [ ] Al terminar, vuelve a instalar el de `arm64`. (El Llavero vuelve a preguntar tras cada cambio.)

## 5. Qué contar si algo falla

- Qué hiciste, qué esperabas y qué salió, con el número de la sección y el texto exacto del mensaje.
- El registro de errores: botón «Registro» de la barra de abajo › «Copiar»; o «Abrir carpeta» y el último archivo de
  `~/Library/Application Support/Centinela/logs`.
- Si Centinela se cierra solo: abre la app Consola (⌘ + Espacio y escribe «Consola»), Informes de fallos, y copia el
  informe de Centinela.
