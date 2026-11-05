# GP-200 Custom Editor (Enhanced Community Edition)

An advanced, high-performance Windows desktop editor and studio suite for the **Valeton GP-200** multi-effects processor.  
Developed and maintained by [WillBV26](https://github.com/WillBV26), expanding upon the foundational reverse-engineering work of [Goaltoday](https://github.com/Goaltoday/GP200_Custom_Editor), the firmware mods of [GP-200-FIRMWARE-MODS](https://github.com/Goaltoday/GP-200-FIRMWARE-MODS), and the studio workflow ideas of [rudywidmer/GP200-Studio](https://github.com/rudywidmer/GP200-Studio).

Supports both official **Stock** firmware and community **Custom / MX42** firmware profiles (selectable in *Settings > Editor > General*).

*This project is an independent community development and is not affiliated with, endorsed by, or sponsored by Valeton or Sunvalley Group.*

---

> ⚠️ **PROJECT STATUS & BETA DISCLAIMER / AVISO IMPORTANTE (BETA ACTIVA)**  
> **English:** This software is currently under **active development, testing, and continuous refinement**. It is an experimental community beta and is **not yet recommended for 100% mission-critical live stage use**; synchronization, features, and hardware edge-cases are still being optimized. Always make backups of your presets before syncing.  
> **Español:** Este software se encuentra en **fase experimental (Beta) bajo desarrollo activo, ajuste y refinamiento continuo**. **Aún NO está al 100% para uso definitivo en presentaciones en vivo críticas**; se siguen puliendo y optimizando funciones, sincronizaciones y detalles de hardware. Mantén siempre copias de seguridad de tus presets.

---

<p align="center">
  <img width="1349" alt="GP-200 Custom Editor Interface" src="https://github.com/user-attachments/assets/20e8a7c1-31bc-4ddb-8114-2d00b086231e" />
</p>

---

## ⚡ Capacidades del Programa & Mejoras de este Fork (Features)

### 🔀 Ruteo Dual Chain A/B en Tiempo Real
- **Topología Paralela Completa:** División de la señal en cuatro zonas independientes: Entrada (*IN*), Rama A (*Branch A*), Rama B (*Branch B*) y Salida (*OUT*).
- **Controles Acústicos de Cadena:**
  - `BLEND`: Mezcla continua de 0% a 100% entre ambas ramas paralelas.
  - `ALIGN`: Alineación temporal en sub-muestras estéreo para compensar discrepancias de latencia entre capturas NAM y respuestas de impulso (IR).
  - `PHASE B · invert`: Inversión instantánea de polaridad en 180° sobre la Rama B para erradicar cancelaciones de fase acústicas.
- **Punto de División (Split A/B):** Control interactivo para desplazar el punto de ramificación en la cadena y botón de distribución inteligente balanceada (`Auto A/B`).
- **Sincronización Directa al Pedal:** Botón dedicado `⚡ Sincronizar Cadena en GP-200` que envía el bloque completo de 14 palabras de ruteo directamente a la memoria de la pedalera.

### 🎛️ Amp & Cab Room Studio (Estudio Visual Dedicado de Amplificador y Cabina)
- **Cabezal Valvular Interactivo:** Entorno visual especializado (`AmpCabStudioWindow`) con perillas analógicas de alta sensibilidad para Gain, Bass, Middle, Treble, Presence y Master (con debouncing ultra-rápido de 20 ms).
- **Sección de Dinámica y Noise Gate:** Compuerta de ruido integrada en el módulo de amplificación con umbral en dB y decaimiento (*Decay*) ajustable.
- **Ecualizador Paramétrico Gráfico de 5 Bandas:** Moldeado tonal preciso en 80 Hz, 400 Hz, 1.6 kHz, 4 kHz y 8 kHz con escala dinámica de ±12 dB.
- **Simulador de Sala y Microfonía de Estudio:** Selección de 4 micrófonos clásicos (*Dyn 57*, *Ribbon 121*, *Cond 87*, *Dyn 421*), filtros de corte de graves (*Low Cut* 20-200 Hz), corte de agudos (*Hi Cut* 3-16 kHz), distancia y balance de mezcla dual.

### 🔄 Morfismo Dinámico de Bloques (Slot Morphing)
- **Intercambio Dinámico de Módulos:** Menú contextual al hacer clic derecho sobre cualquier bloque del rig que permite mutar e intercambiar cualquier posición de la cadena entre los 11 módulos (WAH, PRE, DST, AMP, NR, CAB, EQ, MOD, DLY, RVB, VOL) manteniendo la coherencia de la señal y el enrutamiento.

### 🧠 AI Preset Studio 100% Real (Basado en LLMs de Última Generación)
- **Sin Generadores Sintéticos Falsos:** Se eliminan los algoritmos heurísticos locales; el 100% de las propuestas es generado por modelos de lenguaje reales analizando el mapa oficial de parámetros y algoritmos del pedal.
- **Modelos Oficiales de Vanguardia:**
  - **Google Gemini:** Prioriza por defecto `gemini-2.5-flash-lite` (mayor cuota, menor latencia y sin bloqueos 429), seguido de `gemini-2.5-flash` y `gemini-2.5-pro`.
  - **Anthropic Claude & OpenRouter:** Integración de API completa con validación dinámica de modelos activos.
- **Búsqueda Web de Rigs de Artistas:** Extracción contextual de amplificadores, cajas y efectos reales del artista o canción para guiar a la IA.
- **Inyección Directa en Slot de Prueba:** Envío inmediato al pedal en posición de prueba (por defecto 58-D) con respaldo automático previo en disco.
- **Bancos de 4 Escenas (A/B/C/D) y Estructura Musical:** Generación de escenas para directos y presets por secciones de canción (Clean, Crunch, Dist, Lead).
- **Refinamiento en Lenguaje Natural:** Opciones interactivas de ajuste tonal (*"más cuerpo en graves"*, *"delay en corchea con punto"*, *"convertir a Dual Chain A/B"*).
- **Seguridad Criptográfica:** Credenciales protegidas con DPAPI de Windows en el almacenamiento local del usuario (`%LocalAppData%`).

### 🎵 Analizador Acústico DSP Multi-Formato
- **Compatibilidad de Audio:** Carga y decodificación directa de pistas en `.mp3`, `.wav`, `.m4a` y `.aac`.
- **Extracción de Parámetros FFT:** Motor DSP local que calcula factor de cresta dinámico (clasificación Clean, Crunch o High-Gain), equilibrio en 6 bandas de frecuencia (Sub-Bass, Bass, Low-Mids, High-Mids, Presence, Air), centroide espectral y estimación de tempo en BPM.

### 🔊 Medidor de Sonoridad BS.1770 LUFS & Auto-Nivelación
- **Monitoreo en Tiempo Real:** Medición de nivel pico (*True Peak*), nivel RMS y sonoridad en LUFS Momentáneo e Integrado mediante captura de audio de Windows (WASAPI / Loopback).
- **Auto-Nivelación Activa con 1 Clic:** Botón `⚡ Auto-Nivelar activo a -14 LUFS / -16 LUFS` que calcula la diferencia en dB respecto a los estándares de emisión y ajusta en caliente el volumen general en la memoria local y en la GP-200.

### 📦 Gestión de 32 Slots NAM & SnapTone .CLO
- **32 Slots de Hardware NAM:** Gestor desbloqueado para cargar modelos de Neural Amp Modeler directamente al pedal.
- **Soporte Dual NAM Concurrente:** Hasta 2 bloques NAM simultáneos por preset bajo firmware MX42.
- **Puente NamToClo Integrado:** Conversión automática a modelos SnapTone `.clo` byte-exactos (cabecera `VTSI`, 8840 bytes exactos con verificación CRC16).
- **Cargador de Respuestas de Impulso (IR):** Importación de archivos `.wav` a 44.1 kHz y 48 kHz con explorador de biblioteca local.

### 🎯 Afinador Estroboscópico de Estudio & Asignación SFU1
- **Afinador de Alta Precisión:** Interfaz estroboscópica gráfica fluida, visualización de nota musical en formato gigante, frecuencia exacta en Hz y medidor VU de entrada.
- **Estado del Afinador:** Si la entrada seleccionada está en silencio, muestra "Sin señal · toca una cuerda" en vez de dejar vacía la pantalla.
- **Gestión de Footswitches (Protocolo SFU1):** Configuración visual de pulsadores en modos Patch, Stomp y User con matriz de control CTRL 8x11 y sincronización directa de 4 escenas en un solo envío.

### 🖥️ Interfaz Moderna Dark Pro & Cero Parpadeo (Anti-Flicker)
- **Motor Anti-Flicker:** Actualización granular in-place. La conmutación de bypass, el cambio de bloques seleccionados y el movimiento de perillas no destruyen ni reconstruyen la ventana.
- **Ajustes con Navegación Lateral:** Settings separa claramente las preferencias del editor (idioma, zoom, perfil, datos y herramientas locales) de los ajustes del pedal (general, Footswitch, EXP, EQ, MIDI y ritmos). Las secciones del pedal se leen al abrirlas; no se muestra una escritura de firmware.
- **Sin Opciones Duplicadas:** El zoom, el acceso a Footswitch y la inspección BIN se administran desde Settings, no se repiten en la barra de inicio. La configuración de proveedores IA permanece en su flujo real de AI Preset Studio.
- **Zoom Dinámico y Escalado:** Control persistente del 75% al 150% en pasos de 5%, 75% recomendado por defecto en instalaciones nuevas y preferencias guardadas sin sobrescribir la elección del usuario; porcentaje indicado en Settings y escala compartida con ventanas emergentes.
- **Desplazamiento con Rueda:** Los diálogos y paneles internos desplazan la sección bajo el puntero con la rueda del mouse; los selectores y sliders conservan su interacción propia.
- **Amp & Cab Room Adaptable:** Las tarjetas de amplificador, EQ/noise gate y cabina se ajustan al ancho disponible; los controles de AMP y microfonía se reorganizan en varias filas en ventanas más estrechas.
- **Diagnóstico de Audio:** Afinador y medidor muestran la entrada seleccionada, backend de captura, errores/fallback y estado de espera. La recepción de audio real depende de elegir una entrada conectada con señal; el loopback solo recoge audio que reproduce el PC.
- **Tema Oscuro Universal Windows 11:** Plantillas personalizadas para todos los `ComboBox`, `ListBox`, `TextBox` y ventanas emergentes, garantizando legibilidad total sin textos blancos sobre fondos blancos.
- **Transporte MIDI WinMM Resiliente:** Detección de puertos robusta que prioriza los endpoints principales de la GP-200 y descarta puertos virtuales secundarios.

---

## 🔒 Seguridad Hardware (Anti-Brick)

- **Validación Estricta de Integridad:** Toda inyección `.clo` exige validación estricta de 8840 bytes con cabecera `"VTSI"` y CRC16.
- **Respaldos Automáticos:** Antes de cualquier escritura en la GP-200, el editor guarda automáticamente una copia de seguridad del preset en disco (`Backups/`).
- **Exclusión de Funciones Críticas:** *Las actualizaciones de firmware y el formateo de fábrica quedan deliberadamente excluidos para evitar cualquier riesgo de bloqueo del dispositivo.*

---

## 🛠️ Compilación y Ejecución desde Código Fuente

### Requisitos
- Windows 10/11 x64
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)

### Comandos de Terminal

```powershell
# Clonar el repositorio
git clone https://github.com/WillBV26/GP200_Custom_Editor.git
cd GP200_Custom_Editor

# Compilar en modo Release
dotnet build Editor\Gp200.Replica.csproj -c Release

# Ejecutar el editor
dotnet run --project Editor -c Release

# Ejecutar el arnés automatizado de pruebas de integridad (18 pruebas de sistema)
dotnet run --project Editor -c Debug -- --self-test
```

---

## 📜 Créditos y Agradecimientos

- **[WillBV26](https://github.com/WillBV26):** Arquitectura y desarrollo de este fork mejorado, Ruteo Dual Chain A/B, Amp & Cab Room Studio, Slot Morphing, AI Preset Studio 100% real, analizador DSP multi-formato, medidor BS.1770 LUFS y motor Anti-Flicker.
- **[Goaltoday](https://github.com/Goaltoday):** Proyecto original `GP200_Custom_Editor`, ingeniería inversa del protocolo MIDI/SysEx y firmware mods [GP-200-FIRMWARE-MODS](https://github.com/Goaltoday/GP-200-FIRMWARE-MODS) (extensiones MX42).
- **[rudywidmer](https://github.com/rudywidmer):** Inspiración de diseño, categorización de sonidos y flujo de trabajo web ([GP200-Studio](https://github.com/rudywidmer/GP200-Studio)).
- **[Steven Atkinson](https://github.com/sdatkinson/neural-amp-modeler):** Motor y algoritmos abiertos de Neural Amp Modeler (NAM).
- **Comunidad:** Usuarios y colaboradores de `r/ValetonGP2OO`.
