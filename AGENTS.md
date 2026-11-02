# GP200 Community Editor (GP200 Replica) — AI Master Architecture & Domain Specification

## 1. Visión del Proyecto
Este proyecto (`Editor/` en C# .NET / WPF para Windows x64) es el centro de control definitivo para la pedalera Valeton GP-200. Unifica:
1. La arquitectura de protocolo SysEx/MIDI y extensiones MX42 de **Goaltoday** (`GP200_Custom_Editor`, `GP-200-FIRMWARE-MODS`, `NamtoClo`).
2. La experiencia visual interactiva, gestión por lotes (*Bulk Apply*) y usabilidad en vivo de **kabir0st/gp200-studio**.
3. Las validaciones de integridad de firmware y mejoras de flujo de trabajo documentadas por **tntexplosivesltd** (`gp200-reversing.hashnode.dev` / `gp200-patcher`) y la comunidad de `r/ValetonGP2OO`.

## 2. Mapa del Código Fuente (`Editor/Source/`)
- `Transport/ReadOnlyMidi.cs`: Comunicación WinMM (`midiInGetNumDevs`, `midiOutGetNumDevs`, `midiInGetDevCapsW`, `midiOutGetDevCapsW`, envío/recepción SysEx).
- `UI/ChainControls.cs`: Gestión de cadena dual paralela A/B (`enableChain`, `chainOptions`, `ChainRouting`, `nativeChainModes`, `chainSplits`, `BLEND`, `ALIGN`, `PHASE B · invert`, `RenderChainControls()`, `UpdateChainInteraction()`).
- `UI/ModernWorkspace.cs`: Interfaz principal, barra superior (`connectionBadge`, `enableChain`, `SceneToolbar`, `Load IR`, `Load SnapTone`, `Load NAM`, `Settings`), lienzo de los 11 bloques (`WAH`, `PRE`, `DST`, `VOL`, `AMP`, `NR`, `CAB`, `EQ`, `MOD`, `DLY`, `RVB`) y `ParallelChainRibbon`.
- `UI/EditorWindow.cs`: Clase parcial principal (`Edit()`, `liveEdits.CommitAsync()`, `Render()`, `RefreshLibrary()`, sincronización USB y guardado).
- `UI/EditorWorkspace.cs`: Biblioteca de 256 presets (64 bancos A-D), importación/exportación `.prst` y `.gpscene`.
- `Services/BankService.cs`: Operaciones de lectura/escritura en los 256 slots con copias de seguridad automáticas previas (`LastBackupDirectory`).
- `Services/Mx42NamService.cs` & `Model/CloDocument.cs`: Inyección de modelos NAM y SnapTone `.clo` (cabecera `VTSI`, 8840 bytes exactos, estructura A128/B1024).
- `Model/SceneFootswitchProtocol.cs` & `UI/SceneToolbar.cs` & `UI/SceneFootswitchSettings.cs`: Protocolo `SFU1` para hasta 4 escenas y asignaciones de footswitch.
- `Model/FirmwareInspector.cs` & `UI/HelpWindow.cs`: Auditoría estática de binarios de firmware (`FRMW`).
- `Infrastructure/UiText.cs`: Diccionario bilingüe ES/EN.

## 3. Reglas Críticas de Ingeniería (Inquebrantables)
1. **Detección MIDI Universal y Resiliente (`ReadOnlyMidi.cs`):**
   - En Windows, el driver USB registra `"Valeton GP-200"` y un endpoint secundario `"Valeton GP-200 Subdevice"`.
   - PROHIBIDO usar `caps.Name == "GP-200"` o lanzar error cuando `matches.Count != 1`.
   - Filtrar priorizando nombres que contengan `"GP-200"` o `"Valeton"` y que NO contengan `"Subdevice"`. Si no hay coincidencia exacta, usar el primer dispositivo MIDI válido disponible.
2. **Solución de Raíz al Bug de `Enable CHAIN` y Palabras 13/14 (`ChainControls.cs` & `EditorWindow.cs`):**
   - El receptor de parámetros en vivo del firmware (`liveEdits`) NO publica las palabras 13 y 14 (`CHAIN words 13/14`: `FxLoopMode`, `BLEND`, `ALIGN`, `PHASE B`, `RoutingRaw`) en caliente si no está presente la extensión USB en tiempo real.
   - PROHIBIDO lanzar el `MessageBox` / `UiDialogs.Show` bloqueante (`"MX42's native parameter receiver does not publish CHAIN words 13/14..."`) y PROHIBIDO revertir o cancelar la edición en el documento local (`current.Document`).
   - Cuando el usuario active `Enable CHAIN` o ajuste `BLEND`, `ALIGN`, `PHASE B` o el punto de `Split A/B`:
     a) El cambio DEBE aplicarse y conservarse en `current.Document` en memoria local.
     b) Al cambiar de bloque (`AMP`, `CAB`, `DLY`, etc.) o de pestaña, `RenderChainControls()` DEBE leer el estado modificado en `current.Document` (o en un estado persistente de sesión por `LibraryEntry`) para que `enableChain.IsChecked` NUNCA se apague solo.
     c) Si la edición en vivo rechaza las palabras 13/14, capturar esa condición silenciosamente en `Edit()` / `liveEdits.CommitAsync()`, conservar el documento modificado, mostrar en `status.Text`: `"Ruteo Dual A/B aplicado en el editor. Pulsa 'Guardar en GP-200' o 'Sincronizar Cadena' para escribirlo en el pedal"`, y ofrecer un botón directo en `chainOptions` que envíe el preset completo al slot activo.
3. **Desbloqueo Visual e Interactividad Total (WPF UX):**
   - `enableChain.Visibility` debe ser siempre `Visibility.Visible`.
   - `enableChain.IsEnabled` debe ser `true` siempre que exista un preset cargado (`current != null`), sin bloquearlo por `!Mx42ChainUsb.Available`.
   - El panel `chainOptions` (`BLEND`, `ALIGN`, `PHASE B`, `Split A/B`) debe tener `IsEnabled = true` siempre que `enableChain.IsChecked == true`.
   - Todos los controles interactivos deben tener `Cursor = Cursors.Hand` y `ToolTipService.ShowOnDisabled="True"`.
4. **Seguridad Hardware (Anti-Brick):**
   - Mantener intactas las validaciones de `CloDocument.cs` (`0x2288` / 8840 bytes, `"VTSI"u8`) y los respaldos automáticos de `BankService.cs` antes de cualquier escritura persistente en la GP-200.