# Prueba de las mejoras de vídeo (v1.4)

Qué comprobar en Windows, macOS y Linux después de las mejoras de vídeo. Activa Ver › Mostrar estadísticas para ver la línea `fps · ms · GPU · conv. … ms · pintado …`.

## 1. Pintado con la tarjeta gráfica

- [ ] La línea de estadísticas dice `pintado GPU` en todas las cámaras.
- [ ] La imagen se ve derecha (no al revés ni en espejo), sin bandas, sin colores cambiados y sin inclinarse.
- [ ] Al cambiar de vista (cuadrícula, cámara grande, pantalla completa) las cámaras vuelven a verse en menos de un segundo.
- [ ] El zoom con la rueda y el arrastre funcionan como antes, y con zoom la imagen se ve **más nítida** que en la 1.3.
- [ ] Al pasar el ratón por encima siguen saliendo los botones de la cámara, y el clic, el doble clic y arrastrar para cambiar de sitio funcionan.
- [ ] En la ventana «Añadir cámara», la vista previa se ve bien.
- [ ] Con 9 a 16 cámaras abiertas y `pintado GPU`, la ventana sigue fluida (al moverla, hacer zoom o abrir menús) y las imágenes no van a saltos.
- [ ] Después de bloquear y desbloquear la sesión, suspender y reanudar el equipo, y mover la ventana a otro monitor (con otra escala, si lo hay), el vídeo vuelve a verse solo.

Para comparar con el pintado de antes, arranca Centinela con la variable `CENTINELA_CPU_VIDEO=1` (la línea dirá `pintado CPU`):

- Windows (PowerShell): `$env:CENTINELA_CPU_VIDEO=1; & "C:\ruta\a\Centinela.exe"`. La variable se queda puesta en esa ventana de PowerShell: para volver a probar `pintado GPU`, abre otra ventana o ejecuta antes `Remove-Item Env:CENTINELA_CPU_VIDEO`.
- macOS (Terminal, con Centinela instalado en Aplicaciones y cerrado antes con ⌘Q): `CENTINELA_CPU_VIDEO=1 /Applications/Centinela.app/Contents/MacOS/Centinela`
- Linux: `CENTINELA_CPU_VIDEO=1 centinela`

- [ ] Con las mismas cámaras abiertas, el uso del procesador (Administrador de tareas, Monitor de Actividad o `top`) es menor con `pintado GPU` que con `pintado CPU`.

## 2. Colores

- [ ] Los colores se parecen más a los de la web de la cámara (o a VLC) que en la 1.3: sobre todo rojos y verdes en las cámaras HD.
- [ ] Las capturas (📷) tienen los mismos colores que la imagen en directo.

## 3. Rendimiento

- [ ] Con las mismas cámaras, el número de `conv.` es menor con `pintado GPU` que con `CENTINELA_CPU_VIDEO=1` (`pintado CPU`): con la tarjeta gráfica el procesador solo cambia el tamaño de la imagen.
- [ ] La cámara grande y la pantalla completa se ven algo más nítidas (filtro bicúbico).

## 4. Cámaras por UDP

- [ ] En las cámaras configuradas con UDP aparecen menos manchas grises o imágenes rotas al llegar las imágenes completas.

En Linux el sistema limita el tamaño del búfer de recepción; para probarlo, ejecuta antes `sudo sysctl -w net.core.rmem_max=4194304` (vuelve a su valor normal al reiniciar).

## 5. Sin cambios

- [ ] La detección de movimiento avisa igual que antes (marco rojo y registro).
- [ ] Grabar, el sonido y «Suavizar imagen» funcionan igual.
- [ ] Las cámaras entrelazadas (canales analógicos de un DVR o NVR) se ven como en la 1.3.

Si algo falla con `pintado GPU`, apunta el sistema, la tarjeta gráfica y qué se ve; con `CENTINELA_CPU_VIDEO=1` Centinela funciona como antes mientras se corrige.
