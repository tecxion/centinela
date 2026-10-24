# Prueba de las mejoras de vídeo (v1.4)

Qué comprobar en Windows, macOS y Linux después de las mejoras de vídeo. Activa Ver › Mostrar estadísticas para ver la línea `fps · ms · GPU · conv. … ms · pintado …`.

## 1. Pintado con la tarjeta gráfica

- [ ] La línea de estadísticas dice `pintado GPU` en todas las cámaras.
- [ ] La imagen se ve derecha (no al revés ni en espejo), sin bandas, sin colores cambiados y sin inclinarse.
- [ ] Al cambiar de vista (cuadrícula, cámara grande, pantalla completa) las cámaras vuelven a verse en menos de un segundo.
- [ ] El zoom con la rueda y el arrastre funcionan como antes, y con zoom la imagen se ve **más nítida** que en la 1.3.
- [ ] Al pasar el ratón por encima siguen saliendo los botones de la cámara, y el clic, el doble clic y arrastrar para cambiar de sitio funcionan.
- [ ] En la ventana «Añadir cámara», la vista previa se ve bien.

Para comparar con el pintado de antes, arranca Centinela con la variable `CENTINELA_CPU_VIDEO=1` (la línea dirá `pintado CPU`):

- Windows (PowerShell): `$env:CENTINELA_CPU_VIDEO=1; & "C:\ruta\a\Centinela.exe"`
- macOS: `CENTINELA_CPU_VIDEO=1 /Applications/Centinela.app/Contents/MacOS/Centinela`
- Linux: `CENTINELA_CPU_VIDEO=1 centinela`

- [ ] Con las mismas cámaras abiertas, el uso del procesador (Administrador de tareas, Monitor de Actividad o `top`) es menor con `pintado GPU` que con `pintado CPU`.

## 2. Colores

- [ ] Los colores se parecen más a los de la web de la cámara (o a VLC) que en la 1.3: sobre todo rojos y verdes en las cámaras HD.
- [ ] Las capturas (📷) tienen los mismos colores que la imagen en directo.

## 3. Rendimiento

- [ ] El número de `conv.` baja respecto a la versión anterior en la cámara grande y en pantalla completa (se reparte en varios núcleos).
- [ ] La cámara grande y la pantalla completa se ven algo más nítidas (filtro bicúbico).

## 4. Cámaras por UDP

- [ ] En las cámaras configuradas con UDP aparecen menos manchas grises o imágenes rotas al llegar las imágenes completas.

## 5. Sin cambios

- [ ] La detección de movimiento avisa igual que antes (marco rojo y registro).
- [ ] Grabar, el sonido y «Suavizar imagen» funcionan igual.

Si algo falla con `pintado GPU`, apunta el sistema, la tarjeta gráfica y qué se ve; con `CENTINELA_CPU_VIDEO=1` Centinela funciona como antes mientras se corrige.
