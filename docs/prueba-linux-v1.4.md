# Centinela v1.4 — prueba en Linux

Recórrela en la máquina virtual con Ubuntu 24.04 (escritorio GNOME, con la extensión de iconos de la bandeja
activada). Es la misma lista que `docs/paridad-v1.4.md` (la de Windows), cambiada a lo que se ve en Linux, más las
cosas que solo existen en Linux.

Cómo usarla: haz lo que dice cada punto y comprueba que ves lo que se indica. Si algo no sale igual, apunta el número
de la sección, lo que ha pasado y, si sale, el texto exacto del mensaje. Las órdenes `así` se escriben en una
Terminal (tecla Super y escribe «Terminal») y se confirman con Intro.

## 1. Preparar la máquina virtual

- [ ] En Windows, sube la rama para poder bajarla en la máquina virtual: `git push -u origin feat/v1.4`.
      (Otra forma: copiar la carpeta del proyecto a la máquina virtual y saltarte el `git clone` de abajo.)
- [ ] En la máquina virtual, instala lo necesario:
      `sudo apt update && sudo apt install git curl xz-utils dpkg-dev imagemagick`
      (`imagemagick` es opcional: sirve para que el icono quede con el tamaño justo).
- [ ] Instala .NET 10: `sudo apt install dotnet-sdk-10.0`. Si dice que no encuentra el paquete, sigue las
      instrucciones oficiales de Microsoft para Ubuntu 24.04: https://learn.microsoft.com/dotnet/core/install/linux-ubuntu
      Comprueba con `dotnet --version`: empieza por `10.`.
- [ ] Baja el código: `git clone -b feat/v1.4 https://github.com/tecxion/centinela.git` y entra: `cd centinela`.
- [ ] Baja FFmpeg: `bash tools/get-ffmpeg.sh`. Termina con «FFmpeg libraries in …/ffmpeg/lib» y
      `ls ffmpeg/lib` muestra archivos como `libavcodec.so.62` (el número puede cambiar).
- [ ] Crea el paquete: `bash tools/build-deb.sh`. Tarda unos minutos y termina con
      «Paquete creado: dist/centinela_1.3.0_amd64.deb» (pone 1.3.0 hasta que se cambie la versión al final).
- [ ] Instálalo: `sudo apt install ./dist/centinela_*.deb`. Sin errores.
- [ ] Comprueba que el paquete trae FFmpeg y el sonido: `ls /opt/centinela/ffmpeg` muestra `libavcodec.so.…`,
      `libavformat.so.…` y los demás, y `ls /opt/centinela/libopenal.so*` muestra un archivo.

Para instalar una versión nueva más adelante: repite `git pull`, `bash tools/build-deb.sh` y el `sudo apt install`.
Para quitarlo: `sudo apt remove centinela`.

## 2. Arrancar

- [ ] Abre Centinela desde el menú de aplicaciones (tecla Super y escribe «Centinela»). En el menú se ve el icono de
      Centinela (no un icono genérico).
- [ ] Se ve la ventana con el fondo gris grafito alrededor de las cámaras. La primera vez dice «No hay cámaras» y
      tiene el botón «Añade tu primera cámara».
- [ ] Con Centinela abierto, en el panel de la izquierda (el dock) sale el icono de Centinela, uno solo, no un
      icono genérico aparte.
- [ ] En una Terminal: `xprop WM_CLASS` y haz clic en la ventana de Centinela. Apunta la línea que sale (por
      ejemplo `WM_CLASS(STRING) = "…", "Centinela"`). El segundo valor debe ser exactamente `Centinela`. Si `xprop`
      no existe: `sudo apt install x11-utils`.
- [ ] Cierra Centinela con «Salir» y ábrelo otra vez: la ventana aparece en el mismo sitio y con el mismo tamaño.
- [ ] Con Centinela abierto, ábrelo otra vez desde el menú de aplicaciones, y otra vez escribiendo `centinela` en
      una Terminal. No se abre una segunda ventana: se muestra la que ya estaba (también si estaba escondida en la
      bandeja). La orden de la Terminal termina sola enseguida.
- [ ] Pulsa F11. La ventana ocupa toda la pantalla; F11 o Esc la devuelven a su tamaño.

## 3. Contraseñas y llavero

Centinela guarda las contraseñas de las cámaras en el llavero de GNOME. Para verlo, abre «Contraseñas y claves»
(tecla Super y escribe «Contraseñas»; si no está: `sudo apt install seahorse`).

- [ ] Añade una cámara con su contraseña (sección 4). En «Contraseñas y claves», dentro de «Inicio de sesión», sale
      «Centinela: contraseña de la cámara …» (con un código largo al final).
- [ ] Elimina esa cámara en Centinela: su entrada desaparece de «Contraseñas y claves» (pulsa F5 para refrescar).
- [ ] Llavero bloqueado: en «Contraseñas y claves», clic derecho en «Inicio de sesión» › «Bloquear». Edita una
      cámara y guarda con una contraseña nueva. GNOME pide la contraseña del llavero; escríbela. La cámara se guarda y
      la contraseña aparece en el llavero.
- [ ] Bloquéalo otra vez, edita una cámara, guarda y esta vez pulsa «Cancelar» en la ventana de GNOME. La cámara se
      guarda igual (la contraseña va a un archivo cifrado) y sale una sola vez el aviso «Contraseñas»: «Las
      contraseñas se guardan cifradas en un archivo accesible solo por tu usuario.» GNOME no vuelve a preguntar por
      cada cámara. Este aviso sale una única vez en total: si ya salió en otra prueba, es normal que no salga.
- [ ] Tras lo anterior, en una Terminal: `ls -l ~/.local/share/centinela/secret.key`. La línea empieza por
      `-rw-------` (solo tu usuario puede leerlo).
- [ ] Arrancar con el llavero bloqueado: bloquea el llavero, cierra Centinela con «Salir» y ábrelo. GNOME pide la
      contraseña del llavero; escríbela. Las cámaras se ven con su imagen. Apunta cuántos segundos tarda la ventana.
- [ ] Repite, pero pulsa «Cancelar». La ventana sale (apunta si tarda más de unos segundos) y las cámaras cuya
      contraseña está en el llavero dicen «Falta contraseña» con el botón «Editar». Desbloquea el llavero, sal con
      «Salir» y abre Centinela: vuelven a verse.
- [ ] Cámaras copiadas de Windows: cierra Centinela con «Salir». Copia el `cameras.json` de Windows (está en
      `%APPDATA%\Centinela`) a `~/.local/share/centinela/` en la máquina virtual (guarda antes una copia del que hay;
      en «Archivos», Ctrl+H muestra las carpetas ocultas). Abre Centinela: salen las cámaras de Windows, cada una con
      «Falta contraseña» y el botón «Editar». Edítala, escribe la contraseña y se ve.
- [ ] Importar la copia con contraseñas: en Windows, Archivo › Exportar JSON… con contraseñas. Pásala a la máquina
      virtual y en Centinela Archivo › Importar JSON…. Pide la clave; con la clave correcta las cámaras traen su
      contraseña, se ven, y sus contraseñas aparecen en «Contraseñas y claves».

## 4. Añadir cámaras

- [ ] Pulsa «➕ Añadir cámara». Se abre la ventana para escribir nombre, marca, IP, usuario y contraseña.
- [ ] Elige la marca Tapo, Imou u «Otra»: con «Otra», en «Avanzado» se puede escribir la dirección RTSP.
- [ ] Rellena los datos y pulsa «Probar». Tras unos segundos dice la resolución, el códec y si tiene audio de la
      calidad alta y de la baja (o qué falla, por ejemplo «Contraseña incorrecta» o «Sin conexión»).
- [ ] Guarda. La cámara aparece y empieza a verse (esto confirma que FFmpeg se carga desde `/opt/centinela/ffmpeg`
      sin configurar nada más; `echo $LD_LIBRARY_PATH` en una Terminal no muestra nada).

## 5. Buscar en red

- [ ] Pulsa «🔍 Buscar en red». Aparecen las cámaras de tu red; las que ya tienes salen como «Ya añadida».
      (La máquina virtual tiene que estar en la misma red que las cámaras: red «Puente» o «Bridged» en VirtualBox o
      VMware.)
- [ ] Elige una cámara nueva de la lista: se abre la ventana de añadir con la IP ya escrita.
- [ ] Pulsa «📡 Detectar otras redes». Sale una barra de progreso (unos 20–30 segundos) y luego se añaden las redes
      con cámaras, sin marcar, y las demás redes que existan como «sin cámaras ONVIF». El resumen NO termina con
      «No se pudo hacer ping en este sistema…» (si sale, apúntalo: el ping no funciona en Linux).
- [ ] «Marcar todas», «🔍 Buscar» y «Añadir red» (por ejemplo `192.168.2.0/24`) funcionan como en Windows; al volver
      a abrir la ventana, las redes marcadas siguen marcadas.

## 6. Vistas, botones, menú y zoom

- [ ] Vista: Cuadrícula Auto, 1, 4, 9, 16, «Principal + miniaturas» y «Dos principales + miniaturas» colocan las
      cámaras igual que en Windows; arrastrar una cámara sobre otra las cambia de sitio (la de destino en azul).
- [ ] Doble clic en una cámara: pantalla completa en calidad alta; Esc la cierra.
- [ ] Botones de cada cámara (HD / SD, 📷, ⏺, 👁, ✎, 🗑) y clic derecho (Pantalla completa, Captura, Grabar,
      Editar…, Duplicar…, Calidad de la cámara…, Detección de movimiento, Suavizar imagen, Restablecer zoom,
      Eliminar) hacen lo mismo que en Windows. Los emojis de los botones se ven (no salen cuadrados vacíos).
- [ ] Rueda del ratón: acerca hacia el puntero hasta 8 veces; con zoom, arrastrar mueve la imagen; 0 lo quita.

## 7. Sonido

- [ ] En una cámara grande con audio pulsa 🔇: se oye la cámara y el botón pasa a 🔊.
- [ ] Activa el sonido de otra cámara: la primera se calla. Al cerrar la pantalla completa o esconder Centinela
      en la bandeja, el sonido se para.
- [ ] Mientras suena, en Ajustes › Sonido › «Niveles de volumen» sale Centinela (o «OpenAL Soft») y su volumen se
      puede cambiar ahí. El sonido llega por el sistema de sonido de Ubuntu (PipeWire) sin hacer nada más.
- [ ] Opciones de avisos con los sonidos activados (sección 12): se oyen los dos tonos al perder una cámara y los
      dos pitidos con movimiento.

## 8. Grabar, capturas y carpetas

- [ ] Comprueba los nombres de tus carpetas: `cat ~/.config/user-dirs.dirs`. Con Ubuntu en español salen
      `$HOME/Vídeos`, `$HOME/Imágenes` y `$HOME/Documentos`.
- [ ] 📷 en una cámara: la captura PNG está en `~/Imágenes/Centinela` (no en `~/Pictures`).
- [ ] «⏺ Grabar todas» graba todas las cámaras visibles y el botón pasa a «⏹ Detener todas».
- [ ] «📁 Grabaciones» abre la carpeta `~/Vídeos/Centinela` en «Archivos». Dentro hay una carpeta por cámara con
      archivos MKV que se ven en el reproductor de vídeo de Ubuntu (o en VLC).
- [ ] Registro de errores › «Abrir carpeta» abre `~/.local/share/centinela/logs` en «Archivos».

## 9. Bandeja (barra superior)

- [ ] Arriba a la derecha, en la barra superior, sale el icono de Centinela (el logo con sus colores, no un
      cuadrado ni colores raros).
- [ ] Pulsa la X de la ventana. Centinela se esconde y las grabaciones siguen. La primera vez sale una notificación
      «Centinela sigue en la bandeja» («Las grabaciones continúan. Haz clic en el icono para abrirla; «Salir» la
      cierra.»); las siguientes veces ya no sale.
- [ ] Un clic en el icono (o «Abrir» en su menú) vuelve a mostrar la ventana tal como estaba. Si con un clic se
      abre el menú en lugar de la ventana, apúntalo.
- [ ] El menú del icono tiene «Abrir», «Grabar todas» (o «Detener todas» si graba), «Arrancar al iniciar sesión»,
      una raya y «Salir».
- [ ] «Grabar todas» del menú graba todas las cámaras con la ventana escondida. Al volver a abrir el menú pone
      «Detener todas» (el menú se actualiza solo).
- [ ] Mientras graba alguna cámara, el icono lleva un punto rojo; sin grabar, el normal.
- [ ] «Salir» (del menú del icono o de Archivo › Salir) cierra Centinela del todo y el icono desaparece.
- [ ] Graba una cámara y, sin detenerla, pulsa «Salir». El archivo de la grabación se reproduce bien.

## 10. Sin bandeja

- [ ] Sal de Centinela. Abre «Extensiones» (tecla Super y escribe «Extensiones») y desactiva «Ubuntu AppIndicators».
      Abre Centinela: no hay icono arriba.
- [ ] Pulsa la X: la ventana se minimiza (no se cierra) y las grabaciones siguen. Se recupera desde el dock.
- [ ] En el menú Archivo, antes de «Salir», sale la casilla «Arrancar al iniciar sesión» y funciona (sección 11).
- [ ] Con la ventana minimizada, el movimiento y los cortes de conexión salen como notificaciones de GNOME (sección
      12).
- [ ] Vuelve a activar «Ubuntu AppIndicators», sal de Centinela y ábrelo: vuelve el icono.

## 11. Arrancar al iniciar sesión

- [ ] Marca «Arrancar al iniciar sesión» en el menú del icono. Comprueba el archivo:
      `cat ~/.config/autostart/centinela.desktop`. Tiene una línea `Exec=` con `/opt/centinela/Centinela` y `--tray`.
- [ ] Cierra la sesión y vuelve a entrar (o reinicia la máquina virtual). Centinela arranca solo, escondido en la
      bandeja, sin abrir la ventana, y las cámaras vigiladas siguen vigilándose.
- [ ] Con Centinela así arrancado, ábrelo desde el menú de aplicaciones: se muestra la ventana (no se abre otro).
- [ ] Abre «Aplicaciones al inicio» (tecla Super y escribe «inicio»; si no está:
      `sudo apt install gnome-startup-applications`). Centinela está en la lista. Desmárcalo ahí y abre el menú del
      icono de Centinela: la casilla «Arrancar al iniciar sesión» sale ya desmarcada.
- [ ] Márcala otra vez en Centinela y desmárcala: el archivo `~/.config/autostart/centinela.desktop` desaparece.
      Cierra la sesión y entra: Centinela ya no arranca solo.

## 12. Movimiento, notificaciones y avisos

- [ ] Activa 👁 en una cámara y muévete delante. La cámara lleva un marco rojo mientras hay movimiento, que se
      quita unos 3 segundos después de parar.
- [ ] Con la ventana escondida en la bandeja (o minimizada), al haber movimiento sale una notificación de GNOME
      «Detección de movimiento: «Nombre»» con la hora. Un clic en la notificación abre Centinela. (El modo «No
      molestar» de GNOME tiene que estar apagado.)
- [ ] Como mucho una notificación por cámara en cada tiempo de espera de «Opciones de avisos». Con la ventana a la
      vista no sale notificación, pero sí el marco rojo.
- [ ] El botón de abajo pasa a «Movimiento (1)»; el registro de movimiento se abre, «Copiar», «Abrir carpeta» y
      «Vaciar» funcionan.
- [ ] Ver › Opciones de avisos…: sensibilidad, tiempo de espera, horas de silencio (también pasando medianoche) y
      avisos por cámara funcionan como en Windows; una hora no válida («25:00») da error, se marca en rojo y no se
      guarda.

## 13. Copias de seguridad

- [ ] Archivo › Exportar JSON… sin contraseñas y con contraseñas (clave de al menos 8 caracteres, dos veces).
- [ ] Archivo › Importar JSON… con la copia sin contraseñas: añade las nuevas, actualiza las que ya tenías y sale un
      resumen.
- [ ] Cambia algo de una cámara y mira `~/Documentos/Centinela/camaras-copia.json`: se ha actualizado solo y no
      lleva contraseñas.
- [ ] Archivo › Carpeta de copia automática…: elige otra carpeta; la siguiente copia va allí.

## 14. Errores y estadísticas

- [ ] Desenchufa una cámara: dice «Sin conexión · reintentando» y sale el aviso rojo abajo a la derecha; al volver,
      el aviso verde «✓ Nombre: conexión recuperada». Con la ventana escondida, salen como notificaciones de GNOME.
- [ ] Con la ventana a la vista, pulsa un aviso rojo o verde: se abre «Registro de errores». Ninguna línea muestra
      contraseñas.
- [ ] Ver › Mostrar estadísticas: cada cámara muestra `fps · ms · GPU/CPU`. En la máquina virtual (sin tarjeta
      gráfica de verdad) lo normal es «CPU»; no es un fallo.

## 15. Calidad, suavizar, actualizaciones y ayuda

- [ ] Clic derecho › Calidad de la cámara…, Suavizar imagen y «HD / SD» funcionan como en Windows.
- [ ] Ayuda › Buscar actualizaciones…: sale «Versión instalada: …» y el resultado. «Descargar» abre GitHub en el
      navegador. Si hay versión nueva, la línea «Archivo para este equipo» habla de un `.deb`.
- [ ] Ayuda › Manual: habla de la barra superior, de «Arrancar al iniciar sesión», de `Vídeos/Centinela` y de los
      ajustes de sonido del sistema; no nombra Windows. Licencia, Soporte y Acerca de abren su texto.

## 16. Cerrar sesión con Centinela abierto

- [ ] Con Centinela abierto y grabando una cámara, cierra la sesión. GNOME no se queda esperando por Centinela (no
      sale «Centinela impide cerrar la sesión» ni hay que forzar). Al volver a entrar, la grabación se reproduce
      bien.
- [ ] Lo mismo con la ventana escondida en la bandeja.

## 17. Sin llavero ni servicios del escritorio

Esta prueba arranca Centinela como si el sistema no tuviera llavero, bandeja ni notificaciones.

- [ ] Sal de Centinela con «Salir» (si sigue abierto, esta prueba abriría un segundo Centinela). En una Terminal:
      `env -u DBUS_SESSION_BUS_ADDRESS -u XDG_RUNTIME_DIR centinela`
- [ ] La ventana sale en pocos segundos (apunta cuántos). No hay icono arriba y la X minimiza (sección 10).
- [ ] Edita una cámara y guarda su contraseña: se guarda (en el archivo cifrado) y, si el aviso «Contraseñas» no ha
      salido nunca, sale ahora. Puede que no se oiga el sonido en esta prueba; es normal.
- [ ] Cierra con Archivo › Salir y abre Centinela normal desde el menú: todas las cámaras se ven.

## Qué apuntar al terminar

- Los puntos que no han salido como se dice, con el número de la sección.
- La línea de `xprop WM_CLASS` (sección 2) y los segundos de espera de las secciones 3 y 17.
- Si algo falla al arrancar, la salida de `centinela` lanzado desde una Terminal y el último archivo de
  `~/.local/share/centinela/logs`.
