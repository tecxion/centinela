# Centinela v1.4 — lista de comprobación

Recórrela en Windows con el Centinela nuevo (cerrando antes el de siempre). Cuando todo esté marcado, se borra la versión WPF.

Cómo usarla: haz lo que dice cada punto y comprueba que ves lo que se indica. Si algo no sale igual, apunta el
número de la sección y lo que ha pasado. Para no tocar tus cámaras de siempre puedes probar algunas cosas con una
cámara de prueba y borrarla al terminar.

## 1. Arrancar

- [ ] Abre Centinela. Se ve la ventana con el fondo gris grafito alrededor de las cámaras (cada cámara sobre negro)
      y tus cámaras de siempre, con sus nombres y en el orden de siempre.
- [ ] La ventana aparece en el mismo sitio y con el mismo tamaño que tenía la última vez.
- [ ] Con Centinela abierto, ábrelo otra vez desde el acceso directo. No se abre una segunda ventana: se muestra
      la que ya estaba (también si estaba escondida en la bandeja).
- [ ] Pulsa F11. La ventana ocupa toda la pantalla; F11 o Esc la devuelven a su tamaño.
- [ ] Si venías de CamaraWin (README, «Si venías de CamaraWin»): la primera vez que abres Centinela aparecen solas
      las cámaras y los ajustes que tenías en CamaraWin.

## 2. Añadir cámaras

- [ ] Pulsa «➕ Añadir cámara». Se abre la ventana para escribir nombre, marca, IP, usuario y contraseña.
- [ ] Elige la marca Tapo, Imou u «Otra»: con «Otra», en «Avanzado» se puede escribir la dirección RTSP.
- [ ] Rellena los datos de una cámara y pulsa «Probar». Tras unos segundos dice la resolución, el códec y si tiene
      audio de la calidad alta y de la baja (o qué falla, por ejemplo «Contraseña incorrecta» o «Sin conexión»).
- [ ] Guarda. La cámara aparece en la ventana principal y empieza a verse.
- [ ] Sin cámaras (por ejemplo, con una carpeta de datos vacía) la ventana dice «No hay cámaras» y tiene el botón
      «Añade tu primera cámara».
- [ ] Una cámara cuya contraseña guardada no se puede leer en este equipo (por ejemplo, copiada de otro PC):
      la cámara avisa de que falta la contraseña y, al editarla, sale la nota «La contraseña guardada no se puede
      leer en este equipo: escríbela de nuevo.» Si guardas sin escribirla, no se pierde lo que había guardado.

## 3. Buscar en red

- [ ] Pulsa «🔍 Buscar en red». Aparecen las cámaras de tu red; las que ya tienes salen como «Ya añadida».
- [ ] Elige una cámara nueva de la lista: se abre la ventana de añadir con la IP ya escrita.
- [ ] Pulsa «📡 Detectar otras redes». Sale una barra de progreso (unos 20–30 segundos) y luego se añaden a la
      lista de redes las que tienen cámaras, sin marcar, y las demás redes que existan como «sin cámaras ONVIF».
- [ ] «Marcar todas» marca todas las redes; «🔍 Buscar» busca en las marcadas. Al volver a abrir la ventana, las
      redes marcadas siguen marcadas.
- [ ] Escribe una red (por ejemplo `192.168.2.0/24`) y pulsa «Añadir red». Aparece en la lista.

## 4. Vistas

- [ ] En «Vista:» elige Cuadrícula Auto, 1, 4, 9 y 16. Las cámaras se colocan en ese número de huecos.
- [ ] Elige «Principal + miniaturas» (derecha e izquierda). Una cámara grande y las demás pequeñas a un lado.
      Un clic en una miniatura la pone en grande.
- [ ] Elige «Dos principales + miniaturas». Un clic en una miniatura sustituye a la grande que lleva más tiempo sin
      cambiar; arrastrar una miniatura sobre una grande la pone justo ahí. Al cerrar y abrir, siguen las mismas.
- [ ] Arrastra una cámara sobre otra. Cambian de sitio (mientras arrastras, la de destino se marca en azul).
- [ ] Doble clic en una cámara. Se abre a pantalla completa en calidad alta; Esc la cierra.

## 5. Botones de cada cámara

- [ ] Pasa el ratón por una cámara grande: los botones salen abajo en el centro. En una miniatura, arriba a la
      derecha. Al pulsarlos no «encogen».
- [ ] «HD / SD» (solo en grandes): cambia entre calidad alta y baja. Al cerrar y abrir Centinela se recuerda.
- [ ] 📷: hace una captura PNG a resolución completa en `Imágenes\Centinela`.
- [ ] ⏺: empieza a grabar; sale un punto rojo junto al nombre. Otra pulsación la detiene.
- [ ] 👁: activa o apaga la detección de movimiento. Apagada, junto al nombre sale un 👁 tachado.
- [ ] ✎ abre la ventana de editar; 🗑 pregunta y elimina la cámara.

## 6. Clic derecho

- [ ] Clic derecho en una cámara. El menú tiene: Pantalla completa, Captura, Grabar, Editar…, Duplicar…,
      Calidad de la cámara…, Detección de movimiento, Suavizar imagen (1–2 s de retraso), Restablecer zoom y
      Eliminar. Las opciones activas salen marcadas.
- [ ] «Duplicar…» abre la ventana de añadir con los mismos datos y «(copia)» en el nombre.
- [ ] Cada opción hace lo mismo que su botón.

## 7. Zoom

- [ ] En una cámara grande o a pantalla completa, gira la rueda del ratón. La imagen se acerca hacia el puntero,
      hasta 8 veces.
- [ ] Con zoom, arrastra: la imagen se mueve.
- [ ] Pulsa 0 con el puntero encima (o «Restablecer zoom»): vuelve a verse entera. Al cerrar Centinela el zoom no
      se guarda.

## 8. Sonido

- [ ] En una cámara grande con audio sale 🔇. Púlsalo: se oye la cámara y el botón pasa a 🔊.
- [ ] Activa el sonido de otra cámara: la primera se calla (solo suena una a la vez).
- [ ] También funciona a pantalla completa. Al cerrar la pantalla completa o al esconder Centinela en la bandeja,
      el sonido se para.
- [ ] El volumen de Centinela se puede cambiar en el mezclador de volumen de Windows.

## 9. Grabar y capturas

- [ ] «⏺ Grabar todas» empieza a grabar todas las cámaras visibles y el botón pasa a «⏹ Detener todas».
- [ ] «📁 Grabaciones» abre `Vídeos\Centinela`. Dentro hay una carpeta por cámara con archivos MKV que se ven
      en VLC o en el reproductor de Windows.
- [ ] Las capturas están en `Imágenes\Centinela`.

## 10. Bandeja del sistema

- [ ] Pulsa la X de la ventana. Centinela se esconde junto al reloj y las grabaciones siguen.
- [ ] La primera vez que se esconde sale el globo «Centinela sigue en la bandeja» («Las grabaciones continúan.
      Doble clic en el icono para abrirla; «Salir» la cierra.»). Las siguientes veces ya no sale.
- [ ] Doble clic en el icono (o «Abrir» en su menú): vuelve la ventana tal como estaba.
- [ ] Clic derecho en el icono. El menú tiene «Abrir», «Grabar todas» (o «Detener todas» si graba), «Arrancar con
      Windows», una raya y «Salir».
- [ ] «Grabar todas» del menú graba todas las cámaras, aunque la ventana esté escondida.
- [ ] Mientras graba alguna cámara, el icono de la bandeja lleva un punto rojo; sin grabar, el icono normal.
- [ ] «Salir» (del menú de la bandeja o de Archivo › Salir) cierra Centinela del todo y el icono desaparece.
- [ ] Graba una cámara y, sin detener la grabación, pulsa «Salir». El archivo de la grabación se reproduce bien.

## 11. Arrancar con Windows

- [ ] Marca «Arrancar con Windows» en el menú de la bandeja y reinicia el PC (o cierra la sesión y vuelve a
      entrar). Centinela arranca solo, escondido en la bandeja, sin abrir la ventana, y las cámaras vigiladas siguen
      vigilándose.
- [ ] Desmárcalo y reinicia: Centinela ya no arranca solo.
- [ ] Si tenías marcado «Arrancar con Windows» en la versión de siempre, sigue marcado en la nueva y arranca la
      nueva.

## 12. Detección de movimiento

- [ ] Activa 👁 en una cámara y muévete delante. La cámara lleva un marco rojo mientras hay movimiento (en la
      miniatura, en grande y a pantalla completa) que se quita unos 3 segundos después de parar.
- [ ] Con la ventana minimizada o escondida en la bandeja, al haber movimiento sale un aviso de Windows
      «Detección de movimiento: «Nombre»» con la hora. Un clic en el aviso abre Centinela.
- [ ] Sale como mucho un aviso por cámara en cada tiempo de espera (el que elegiste en «Opciones de avisos»).
- [ ] Con la ventana abierta y a la vista no sale aviso, pero sí el marco rojo.
- [ ] Pulsa «Salir» mientras hay movimiento delante de una cámara: al volver a abrir, ese movimiento está en el
      registro de movimiento.

## 13. Registro de movimiento

- [ ] Tras un movimiento, el botón de la barra de abajo pasa a «Movimiento (1)» (o el número que toque).
- [ ] Púlsalo. Se abre el registro con hora, cámara, duración y cambio máximo, y el contador vuelve a «Movimiento».
- [ ] «Copiar» copia las líneas, «Abrir carpeta» abre la carpeta de registros y «Vaciar» lo vacía.

Nota: «Copiar» copia las filas en el orden en que se ven (la versión de siempre las copiaba en el orden en que se
seleccionaron).

## 14. Opciones de avisos

- [ ] Abre Ver › Opciones de avisos…. Hay una tabla con cada cámara: detección, sensibilidad (Baja, Media o
      Alta), tiempo de espera (30 s, 1 min, 5 min o 15 min), avisos de conexión y avisos de movimiento.
- [ ] Activa los sonidos. Al perder la conexión suenan dos tonos que bajan; con movimiento, dos pitidos cortos
      (solo cuando sale el aviso). Son sonidos propios de Centinela, distintos de los de Windows.
- [ ] Pon unas horas de silencio que incluyan la hora actual (pueden pasar de medianoche, por ejemplo de 23:00 a
      07:00). En ese rato no hay avisos ni sonidos, pero el registro y el marco rojo siguen.
- [ ] Escribe una hora no válida (por ejemplo «25:00») y pulsa aceptar: sale un mensaje de error y no se guarda.
- [ ] Desactiva «Avisos de conexión» y «Avisos de movimiento» de una cámara. Desenchúfala y muévete delante: esa
      cámara no avisa (ni con la ventana escondida), aunque el registro y el marco rojo siguen.

## 15. Copias de seguridad

- [ ] Archivo › Exportar JSON… sin contraseñas: se guarda un archivo con la lista de cámaras.
- [ ] Exportar con contraseñas: pide una clave (al menos 8 caracteres, dos veces). Se guarda cifrado.
- [ ] Archivo › Importar JSON… con la copia sin contraseñas: añade las cámaras nuevas y actualiza las que ya
      tenías (misma IP y puerto); al final sale un resumen.
- [ ] Importar la copia con contraseñas: pide la clave; con la clave correcta las cámaras traen su contraseña.
- [ ] Cambia cualquier cosa de una cámara y mira `Documentos\Centinela\camaras-copia.json`: se ha actualizado
      solo, y no lleva contraseñas.
- [ ] Archivo › Carpeta de copia automática…: elige otra carpeta. La siguiente copia automática va allí.

## 16. Actualizaciones

- [ ] Ayuda › Buscar actualizaciones…. Sale la ventana «Actualizaciones» con «Versión instalada: …» y el
      resultado de la comprobación.
- [ ] Si hay una versión nueva, se ven sus notas y la línea «Archivo para este equipo: Centinela-…-windows-x64.exe».
- [ ] «Descargar» abre la página de GitHub en el navegador (Centinela no descarga nada por su cuenta).
- [ ] «Omitir esta versión» hace que no vuelva a avisar de esa versión; «Volver a comprobar» repite la consulta;
      «Comprobar al arrancar» activa o quita la comprobación diaria; «Cerrar» cierra.
- [ ] Con una versión nueva publicada, unos segundos después de arrancar (una vez al día) sale el aviso
      «Centinela … disponible». Con la ventana escondida sale como aviso de Windows y al pulsarlo se abre la ventana
      de actualizaciones.
- [ ] Con la ventana a la vista, pulsa «Ver» en el aviso de actualización: se abre la ventana «Actualizaciones».

Solo se puede comprobar del todo cuando haya publicada en GitHub una versión más nueva que la tuya.

## 17. Errores y registro de errores

- [ ] Desenchufa una cámara (o apaga su wifi). La cámara dice «Sin conexión · reintentando» y abajo a la derecha de
      la ventana sale un aviso rojo. Al volver, un aviso verde «✓ Nombre: conexión recuperada».
- [ ] Con la ventana escondida en la bandeja, esos avisos salen como avisos de Windows.
- [ ] Con la ventana a la vista, pulsa un aviso rojo o verde: se abre «Registro de errores».
- [ ] Pon mal la contraseña de una cámara: dice «Contraseña incorrecta» y tiene el botón «Editar».
- [ ] El botón «Registro» de la barra de abajo muestra el número de errores sin ver («Registro (2)»). Al pulsarlo se
      abre «Registro de errores» con hora, cámara, error, título y detalle, y el número se quita.
- [ ] En el registro, «Copiar» copia las líneas seleccionadas (o todas), «Abrir carpeta» abre la carpeta de
      registros y «Limpiar» lo vacía. Ninguna línea muestra contraseñas.
- [ ] Ayuda › Manual: la sección «Errores frecuentes» explica cada mensaje.

## 18. Estadísticas

- [ ] Ver › Mostrar estadísticas: cada cámara muestra `fps · ms · GPU/CPU`. Se ve también a pantalla completa.
- [ ] Desmárcalo: desaparecen.

## 19. Calidad de la cámara

- [ ] Clic derecho › Calidad de la cámara…. Muestra la resolución, los fps y el bitrate de la calidad alta y la baja.
- [ ] «Recomendados (wifi pobre)» rellena valores ligeros, pero no cambia nada todavía.
- [ ] «Aplicar…» pide confirmación y cambia la cámara; después la imagen sale con los valores nuevos.
- [ ] «Valores originales» vuelve a rellenar los que tenía antes del primer cambio.

## 20. Suavizar imagen

- [ ] Clic derecho › Suavizar imagen (1–2 s de retraso). Junto al nombre sale ⏱ y la imagen va un poco por detrás
      pero sin acelerones tras un corte.
- [ ] Desmárcalo: el ⏱ desaparece. Al cerrar y abrir Centinela se recuerda.

## 21. HD / SD

- [ ] En una cámara grande pulsa «HD / SD»: pasa a la calidad baja (más ligera) y otra vez a la alta.
- [ ] El doble clic abre la pantalla completa en calidad alta aunque la cámara esté en SD.

## 22. Ayuda

- [ ] Ayuda › Manual, Licencia, Soporte y Acerca de Centinela abren sus ventanas con el texto de siempre.

## Comprobaciones visuales pendientes

Estas cosas no se pueden comprobar con las pruebas automáticas; míralas con calma:

- [ ] «Buscar en red»: la cabecera de la lista de cámaras está alineada con las filas (también cuando aparece la
      barra de desplazamiento).
- [ ] «Opciones de avisos»: una hora no válida se marca con borde rojo.
- [ ] Bandeja: el icono se ve bien (con y sin el punto rojo), el menú se cierra al pulsar fuera, y en Windows 11 un
      clic en el globo abre la ventana.
- [ ] Bandeja: reinicia el Explorador de Windows (Administrador de tareas › Explorador de Windows › Reiniciar). El
      icono de Centinela vuelve a aparecer.
- [ ] Arranca Centinela escondido (como al encender el PC con «Arrancar con Windows») y, sin abrir la ventana, pulsa
      «Salir» en el menú de la bandeja. Centinela se cierra del todo.
- [ ] Con Centinela abierto, cierra la sesión de Windows (o apaga el PC). Windows no se queda esperando por
      Centinela y, al volver, las grabaciones hechas se reproducen bien.
