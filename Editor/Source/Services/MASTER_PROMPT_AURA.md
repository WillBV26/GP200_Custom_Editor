# MASTER PROMPT: REINGENIERÍA COMPLETA DE "GP-200 STEM & JAM STUDIO" (LAYOUT BANDLAB + CONEXIÓN AISLADA CON STEMKIT 6-STEMS + FIX ZOOM)

**Objetivo Principal:**
Reemplazar por completo la ventana rota `Aura Jam & Stem Studio` por el nuevo **`GP-200 Stem & Jam Studio`**. 
- **PROHIBIDO usar la palabra "Aura"** en títulos, clases, espacios de nombres o etiquetas visibles (reemplazar todo `"Aura Jam & Stem Studio"` y `"AURA JAM STUDIO"` por **`GP-200 Stem & Jam Studio`**).
- Rediseñar el 100% de la interfaz para replicar la **estructura visual y funcional de BandLab** (Cabeceras de pista a la izquierda + Lienzo multipista con formas de onda a color a la derecha + Barra de transporte unificada arriba + Zona inferior de Drag & Drop), eliminando el mezclador inferior roto y el rectángulo cian vacío.
- Conectar el módulo de forma **aislada y sincrónica** con un entorno externo de **StemKit (`danielravina/stemkit` v0.2.0 / Demucs 6-Stems)** alojado fuera del repositorio (`%LOCALAPPDATA%\GP200_Community_Editor\Engines\StemKit\`), sin agregar gigabytes al proyecto C#.
- Reparar de raíz el funcionamiento del **Zoom Global (`- 100% +`)** y añadir el **Zoom Horizontal de Línea de Tiempo (`🔍-` / `🔍+`)**.

---

## FASE 1: CORRECCIÓN DE ERRORES VISUALES Y REDISEÑO ESTRUCTURAL IDÉNTICO A BANDLAB

### 1. Eliminación de Elementos Rotos de la Versión Anterior
En el archivo de la ventana del estudio DAW:
- **ELIMINA** el panel de mezclador vertical inferior donde los botones `MUTE` y `SOLO` salían cortados (`MUT` / `SOLO`) y **ELIMINA** el cuadro azul cian sólido vacío de la esquina inferior izquierda.
- **ELIMINA** el recuadro gigante central de `"AUTOMATIZACIÓN DE PRESETS Y ESCENAS EN LÍNEA DE TIEMPO"` que tapaba toda la zona de pistas. Transfórmalo en un **Carril Superior Delgado de Marcadores (`⚡ Pista de Automatización GP-200`, `Height="32"`)** justo debajo de la regla de compases (`1, 2, 3, 4...`).
- Corrige el ancho (`MinWidth="68"`, `Padding="6,2"`) del cuadro de texto de `BPM` para que números de 3 dígitos como `150` jamás se corten a `15`.

### 2. Estructura Exacta Tipo BandLab (3 Zonas Limpias en WPF `Grid`)

#### A. Barra Superior de Transporte y Configuración StemKit (`Row="0"`, `Height="56"`)
Diseña una barra superior moderna en una sola fila limpia (estilo BandLab con estética oscura `#080D16` y bordes `#1E3352`):
- **Izquierda (Acciones y Motor StemKit):**
  - Botón `[ + Cargar Audio / URL ]` (permite elegir archivo `.mp3/.wav/.flac` o pegar URL).
  - Botón desplegable `[ ⚙️ Opciones StemKit ▾ ]` que permite elegir antes de separar:
    - **Modelo IA:** `htdemucs_6s (6 Stems: Voz, Batería, Bajo, Guitarra, Piano, Otros)` *(Por defecto)* | `htdemucs_ft (4 Stems HQ)` | `htdemucs (4 Stems Rápido)` | `MDX-Net Karaoke`.
    - **Dispositivo:** `Auto (GPU CUDA / DirectML)` | `CPU`.
    - **Formato de Salida:** `WAV 24-bit` | `FLAC` | `MP3 320k`.
  - Botón destacado en cian `[ ✨ Separar 6 Stems con StemKit ]`.
- **Centro (Transporte Musical Estilo BandLab):**
  - Píldora de Tempo detectado por StemKit: `[ 150 bpm ]` (editable + botón `[ ⚡ Enviar Tap a GP-200 ]`).
  - Píldora de Compás y Tonalidad detectada por StemKit: `[ 4/4 ]` y `[ C min ]` (con selector de Pitch Shift `-6` a `+6` semitonos por si el usuario quiere cambiar el tono de la canción sin desafinar su guitarra).
  - Controles centrales redondeados: `[ ⏮ Inicio ]`, `[ ▶ Play / ⏸ Pausa ]`, `[ 🔴 REC Mi Guitarra ]`, `[ 🔁 Loop ON/OFF ]`, reloj digital grande `[ 00:00.0 / 04:12.5 ]`.
- **Derecha (Volumen Maestro, Exportación y Zoom Funcional):**
  - Slider compacto de `Master Vol (-1.5 dB)`, botón `[ 💾 Exportar Mezcla ]`.
  - **Zoom Global de Ventana (`ZOOM [ - ] 100% [ + ]`)** y **Lupas de Zoom de Ondas (`[ 🔍- ] [ 🔍+ ]`)**.

#### B. Cuerpo Central Dividido en 2 Columnas como BandLab (`Row="1"`, `Height="*"`)
Usa un `Grid` con dos columnas sincronizadas verticalmente mediante un único `ScrollViewer` compartido o `ScrollChanged` enlazado:
- **Columna 0 (`Width="250"`): Panel Izquierdo de Cabeceras de Pistas (`Track Headers`)**
  - Arriba del todo: Botón `[ + Añadir Pista ]` y botón `[ ⚡ + Disparador GP-200 ]`.
  - Debajo, una fila (`Height="84"`) por cada una de las **7 pistas fijas/dinámicas** (6 Stems de StemKit + 1 Pista de Grabación en Vivo de la Valeton GP-200):
    1. **`01 Guitarra (StemKit)`** — Etiqueta de color **Verde Esmeralda (`#10B981`)** — *(Incluye botón rápido `"Silenciar para practicar"`)*.
    2. **`02 Voces (StemKit)`** — Etiqueta de color **Naranja (`#F97316`)**.
    3. **`03 Piano / Keys (StemKit)`** — Etiqueta de color **Ámbar (`#F59E0B`)**.
    4. **`04 Bajo (StemKit)`** — Etiqueta de color **Amarillo (`#EAB308`)**.
    5. **`05 Batería (StemKit)`** — Etiqueta de color **Morado (`#A855F7`)**.
    6. **`06 Otros / Pads (StemKit)`** — Etiqueta de color **Azul (`#3B82F6`)**.
    7. **`07 ⚡ MI GUITARRA GP-200 (En Vivo)`** — Etiqueta de color **Cian Brillante (`#00E5FF`)** con botón de monitoreo de entrada USB y selector rápido de Preset/Escena.
  - **Contenido exacto de cada Cabecera de Pista (Idéntico a BandLab):**
    - Fila superior de la tarjeta: Icono + Nombre de la pista (`01 Guitarra`) + Botón cuadrado **`[ M ]`** (`Width="26", Height="24"`, se ilumina en Rojo `#EF4444` cuando está muteado) + Botón cuadrado **`[ S ]`** (`Width="26", Height="24"`, se ilumina en Amarillo `#F59E0B` cuando está en Solo). **Usa las letras `"M"` y `"S"` como en BandLab para que jamás se corte el texto**.
    - Fila inferior de la tarjeta: Slider horizontal de **Volumen de pista (`-∞ a +6 dB`)** a la izquierda y un control rotatorio compacto (**Knob `L / R` de Paneo**) a la derecha.

- **Columna 1 (`Width="*"`): Lienzo Multipista de Línea de Tiempo y Ondas (`Timeline & Waveform Canvas`)**
  - **Fila Superior 1 (Regla de Tiempo y Compases, `Height="28"`):** Dibuja los números de compás (`1, 2, 3, 4, 5, 6...`) y permite hacer clic o arrastrar para definir la región de **Loop (Bucle A-B)** en color cian translúcido.
  - **Fila Superior 2 (Carril de Automatización GP-200, `Height="28"`):** Muestra banderitas interactivas sobre la línea de tiempo (ej. en `00:45 -> [Scene B: Crunch]`, en `02:10 -> [FS2: Overdrive ON]`). Al hacer doble clic en cualquier punto de este carril, se añade un cambio automático de Preset, Escena (`A/B/C/D`) o Footswitch (`FS1-FS8`) que se enviará por SysEx al pedal cuando el cursor cruce ese punto.
  - **Filas de Carriles de Pistas (`Height="84"` cada una, alineadas al milímetro con su cabecera izquierda):**
    - Fondo con cuadrícula vertical oscura marcando compases y tiempos (como en BandLab).
    - Cuando una pista tiene audio cargado o separado por StemKit, dibuja un bloque redondeado del color de esa pista (Verde, Naranja, Ámbar, Morado, Azul, Cian) con la **Forma de Onda real (`Peak/RMS Waveform Geometry`) renderizada por hardware en WPF** y el nombre del archivo en la esquina superior izquierda del clip.
    - **Cabezal de Reproducción (`Playhead`):** Una línea vertical blanca brillante con triángulo superior que recorre todas las pistas a 60 FPS durante la reproducción y se puede arrastrar con el ratón (`Scrubbing`).

#### C. Zona Inferior de Drag & Drop y Estado de StemKit (`Row="2"`, `Height="52"`)
- Igual a la barra inferior de BandLab: permite **arrastrar y soltar (`AllowDrop="True"`, evento `Drop`)** cualquier archivo `.mp3`, `.wav` o `.flac` directamente desde el Explorador de Windows hacia la ventana.
- Cuando **StemKit** está separando una canción en segundo plano, esta barra muestra la barra de progreso animada:
  `⚙️ StemKit (htdemucs_6s) separando 6 pistas: 68% — Extrayendo Guitarra y Piano... [ Cancelar ]`.

---

## FASE 2: ARQUITECTURA DE CONEXIÓN AISLADA Y SINCRÓNICA CON EL REPOSITORIO `STEMKIT`

### Regla de Oro: Cero Gigabytes dentro del Repositorio `GP200_Custom_Editor`
El editor NO debe incluir archivos pesados de Python/PyTorch ni modelos `.th`/`.onnx` dentro de la solución C#. En su lugar, implementa la clase **`StemKitBridgeService.cs`** con comunicación por procesos aislados:

1. **Instalación y Detección Aislada (`%LOCALAPPDATA%\GP200_Community_Editor\ExternalTools\StemKit\`):**
   - `StemKitBridgeService` comprueba si en esa ruta externa existe la instalación de **StemKit (`v0.2.0+` de `github.com/danielravina/stemkit`)** o su entorno CLI (`demucs` + `ffmpeg` + `yt-dlp`).
   - Si no está instalado, al pulsar `"Separar 6 Stems"` o `"Configurar StemKit"`, abre un asistente automático dentro del editor con 2 opciones de un solo clic:
     - **Opción 1 (`Descargar e Instalar Motor StemKit Aislado Automáticamente`):** Descarga con `HttpClient` y barra de progreso la versión oficial desde las Releases de GitHub (`danielravina/stemkit/releases/tag/v0.2.0`) o el paquete portable en la carpeta `%LOCALAPPDATA%`, dejándolo listo sin que el usuario tenga que configurar nada manualmente.
     - **Opción 2 (`Vincular Carpeta Existente de StemKit`):** Por si el usuario ya lo descargó en otra ruta de su PC.

2. **Ejecución Sincronizada y Transparente desde el Editor (IPC / Background Process):**
   - Cuando el usuario carga una canción en `GP-200 Stem & Jam Studio` y pulsa **`[ ✨ Separar 6 Stems con StemKit ]`**:
     1. `StemKitBridgeService` lanza el motor aislado de StemKit en un proceso secundario sin bloquear la UI (`ProcessStartInfo` con `UseShellExecute = false`, `RedirectStandardOutput = true`, `RedirectStandardError = true`, `CreateNoWindow = true`).
     2. Pasa como argumentos el archivo de entrada, el modelo elegido en la barra superior (`-n htdemucs_6s`), el dispositivo (`-d cuda` o `-d cpu`), el formato de salida y la carpeta de proyecto temporal (`%LOCALAPPDATA%\GP200_Community_Editor\JamProjects\<NombreCancion>\`).
     3. Lee en tiempo real la salida estándar del proceso de StemKit para:
        - Actualizar el porcentaje exacto (`0% -> 100%`) y el tiempo restante en la interfaz de `GP-200 Stem & Jam Studio`.
        - Recibir el **BPM** y la **Tonalidad (Key)** analizados y mostrarlos automáticamente en las píldoras superiores (`[ 150 bpm ]`, `[ C min ]`).
     4. **Carga Automática Inmediata al Terminar:** En cuanto el proceso de StemKit devuelve código de salida `0`, el editor lee automáticamente los 6 archivos generados (`guitar.wav`, `vocals.wav`, `piano.wav`, `bass.wav`, `drums.wav`, `other.wav`), calcula sus formas de onda y los monta sincronizados en los carriles `01` a `06` del lienzo tipo BandLab.

3. **Motor de Mezcla Multipista en Tiempo Real (`MultiTrackAudioEngine.cs`):**
   - Usa `NAudio` / `WASAPI` para reproducir simultáneamente los 6 stems en perfecta sincronía de muestras (*sample-accurate sync*).
   - Cada cambio en los botones `[ M ]` (Mute), `[ S ]` (Solo), slider de volumen o perilla `L/R` de las cabeceras izquierdas ajusta en tiempo real el `MixingSampleProvider` sin cortes ni latencia.
   - Si el usuario activa `[ 🔴 REC Mi Guitarra ]`, captura la entrada de audio USB de la **Valeton GP-200** en el carril `07 ⚡ MI GUITARRA GP-200`, dibujando la onda cian en vivo mientras toca sobre la pista con la guitarra original muteada.

---

## FASE 3: REPARACIÓN DEFINITIVA DEL BOTÓN DE ZOOM (`- 100% +`) EN TODAS LAS VENTANAS

### Por qué no funcionaba el Zoom en la captura anterior y cómo debes arreglarlo:
En la captura anterior, los botones `-` y `+` de `ZOOM 75%` no escalaban la interfaz porque no aplicaban `LayoutTransform` al contenedor raíz real de la ventana.

1. **Implementación Real de `UiScaleManager`:**
   - En `MainWindow`, `JamStudioWindow` y todas las ventanas secundarias, envuelve el contenido completo dentro de un `Grid` raíz llamado `_scalableRootGrid`.
   - Al hacer clic en **`[ - ]`** o **`[ + ]`** (o usar `Ctrl + Rueda del Ratón`):
     ```csharp
     _currentZoom = Math.Clamp(_currentZoom + delta, 0.70, 1.50);
     _scalableRootGrid.LayoutTransform = new ScaleTransform(_currentZoom, _currentZoom);
     _zoomLabel.Text = $"{Math.Round(_currentZoom * 100)}%";
     ```
   - Esto garantiza que al bajar a `75%` o subir a `125%`, **toda la interfaz, textos, pistas y botones escalen proporcionalmente de verdad** en pantalla.
2. **Lupas de Zoom de Línea de Tiempo (`🔍-` y `🔍+`):**
   - Adicionalmente, los botones de lupa sobre la regla de compases cambian la variable `PixelsPerSecond` (de `20 px/s` a `200 px/s`), estirando o contrayendo horizontalmente las formas de onda dentro del `ScrollViewer` derecho exactamente igual que en BandLab.

---

## VERIFICACIÓN FINAL
1. Compila con `dotnet build -c Release` (**0 Errores, 0 Advertencias**).
2. Verifica que no exista en ninguna parte de la interfaz la palabra `"Aura"`.
3. Confirma que la ventana `GP-200 Stem & Jam Studio` tenga las cabeceras de las 7 pistas a la izquierda con botones `[ M ]` y `[ S ]` sin textos cortados, formas de onda de colores a la derecha con cuadrícula de compases estilo BandLab, conexión aislada con las 6 pistas de **StemKit**, y que los botones de Zoom (`-` / `+`) funcionen al 100%.