using System.Text.RegularExpressions;
using System.Windows;
namespace Gp200.Replica;
public static class UiText
{
    public static string Language { get; set; } = "en";
    static readonly Dictionary<string,string> English = Load();
    static Dictionary<string,string> Load()
    {
        var result=Pairs.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(s=>s.TrimEnd('\r').Split('|',2)).ToDictionary(p=>p[0].Replace("\\n","\n"),p=>p[1].Replace("\\n","\n"));
        foreach(var pair in System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Data","ui-en-extra.json")))!)result[pair.Key]=pair.Value;
        return result;
    }
    internal static IReadOnlyDictionary<string,string> EnglishCatalog=>English;
    static readonly KeyValuePair<string,string>[] Patterns=English.Where(p=>p.Key.Contains("{0}")).OrderByDescending(p=>Regex.Replace(p.Key,@"\{\d+}","").Length).ToArray();
    public static string Filter(string filter)=>string.Join("|",filter.Split('|').Select((part,i)=>i%2==0?T(part):part));
    public static string T(string text)
    {
        if(Language!="en")return Spanish.TryGetValue(text,out var spanish)?spanish:text;
        if(English.TryGetValue(text,out var value))return value;
        foreach(var pair in Patterns)
        {
            string pattern="^"+Regex.Replace(Regex.Escape(pair.Key),@"\\\{\d+}","(.*?)")+"$";
            var match=Regex.Match(text,pattern,RegexOptions.Singleline|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
            if(match.Success)return Regex.Replace(pair.Value,@"\{(\d+)}",m=>match.Groups[int.Parse(m.Groups[1].Value)+1].Value);
        }
        return text;
    }
    static readonly Dictionary<string,string> Spanish=LoadSpanish();
    static Dictionary<string,string> LoadSpanish()
    {
        var result=SpanishBuiltIn();
        foreach(var pair in System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Data","ui-es-extra.json")))!)result[pair.Key]=pair.Value;
        return result;
    }
    static Dictionary<string,string> SpanishBuiltIn()=>new()
    {
        ["Input / Output Settings"]="Entrada / salida",["USB Audio Settings"]="Audio USB",["Input Level (dB)"]="Nivel de entrada (dB)",["Input Mode"]="Modo de entrada",
        ["USB Rec Level (dB)"]="Nivel de grabación USB (dB)",["USB Rec Mode L"]="Modo de grabación USB L",["USB Rec Mode R"]="Modo de grabación USB R",["USB Monitor (dB)"]="Monitor USB (dB)",
        ["Auto Cab Match"]="CAB automático",["Device Display"]="Pantalla del equipo",["Signal Chain"]="Cadena de señal",["Patch"]="Preset",
        ["MIDI In Source"]="Fuente de entrada MIDI",["Input CH DIN"]="Canal de entrada DIN",["Input CH USB"]="Canal de entrada USB",["Output CH DIN"]="Canal de salida DIN",["Output CH USB"]="Canal de salida USB",
        ["Clock Source"]="Fuente de reloj",["Clock Out DIN"]="Salida de reloj DIN",["Clock Out USB"]="Salida de reloj USB",["DIN Only"]="Solo DIN",["USB Only"]="Solo USB",["Mixed"]="Mixto",["External"]="Externo",["Internal"]="Interno",
        ["About"]="Acerca de",["Help"]="Ayuda",["Release Notes"]="Notas de versión",["General"]="General",["Mode"]="Modo",["Parallel"]="Paralelo",["Series"]="Serie",["Dry"]="Sin procesar",["Wet"]="Procesado",["Line"]="Línea",
        ["Single Footswitch"]="Pulsador simple",["Double Footswitch"]="Pulsador doble",["None"]="Ninguno",["Initial"]="Inicial",["Wait"]="Esperar",["Tuner"]="Afinador",["Drum"]="Ritmos",["Bank"]="Banco",
        ["Bank +"]="Banco +",["Bank −"]="Banco −",["Patch +"]="Preset +",["Patch −"]="Preset −",["Patch A"]="Preset A",["Patch B"]="Preset B",["Patch C"]="Preset C",["Patch D"]="Preset D",
        ["Drum Patch +"]="Patrón +",["Drum Patch −"]="Patrón −",["Patch Mode"]="Modo preset",["Stomp Mode"]="Modo pedal",["User Mode"]="Modo usuario",
        ["CTRL Setting"]="Asignaciones CTRL",["EXP Setting"]="Asignaciones EXP",["Quick Access"]="Acceso rápido",["Patch Volume"]="Volumen del preset",["Patch Tempo"]="Tempo del preset",["Reset template…"]="Restablecer plantilla…",["Reset footswitch template"]="Restablecer plantilla de footswitch",
        ["Read 256 presets"]="Leer 256 presets",["Full Bank ZIP"]="ZIP de banco completo",["Export selected ZIP"]="Exportar selección ZIP",
        ["All presets"]="Todos los presets",["Favorites"]="Favoritos",["Add to song"]="Añadir a canción",["Remove from setlist"]="Quitar de lista",["Song or setlist name"]="Nombre de la canción o lista",
        ["Local collections could not be loaded: "]="No se pudieron cargar las colecciones locales: ",["Local collections are unavailable."]="Las colecciones locales no están disponibles.",
        ["Select a song list to remove the preset from."]="Selecciona una lista de canciones para quitar el preset.",["The preset is not in this setlist."]="El preset no pertenece a esta lista.",
        ["Preset removed from Favorites."]="Preset eliminado de Favoritos.",["Preset added to Favorites."]="Preset añadido a Favoritos.",["Preset added to setlist: "]="Preset añadido a la lista: ",
        ["Preset removed from setlist: "]="Preset eliminado de la lista: ",["Song setlist"]="Lista de canciones",["Add to Favorites"]="Añadir a Favoritos",["Remove from Favorites"]="Quitar de Favoritos",
        ["AI presets"]="Presets con IA",["Desktop tuner"]="Afinador de audio",["Audio input"]="Entrada de audio",["Start tuner"]="Iniciar afinador",["Stop tuner"]="Detener afinador",
        ["Waiting for audio…"]="Esperando audio…",["No active audio inputs were found."]="No se encontraron entradas de audio activas.",
        ["Select an audio input and start the tuner."]="Selecciona una entrada de audio e inicia el afinador.",
        ["The input level is too low or silent."]="El nivel de entrada es demasiado bajo o está en silencio.",
        ["Input format is not supported by this tuner."]="Este afinador no admite el formato de entrada.",
        ["This is a local audio-input tuner; it does not read the GP-200 firmware tuner."]="Afinador local de entrada de audio; no lee el afinador del firmware de GP-200.",
        ["Could not start audio capture: "]="No se pudo iniciar la captura de audio: ",["In tune"]="Afinado",
        ["Loudness meter"]="Medidor de sonoridad",["Start measurement"]="Iniciar medición",["Stop measurement"]="Detener medición",
        ["NAM source or GP-200 CLO"]="Modelo NAM o CLO GP-200",
        ["Select a NAM model to convert or a GP-200 CLO to upload."]="Selecciona un modelo NAM para convertir o un CLO GP-200 para cargar.",
        ["NAM conversion creates a GP-200 CLO for one of the ten SnapTone slots. Native NAM loading is a separate Custom-firmware feature."]="La conversión NAM crea un CLO GP-200 para uno de los diez slots SnapTone. La carga NAM nativa es una función distinta del firmware Custom.",
        ["A GP-200 CLO uploads to one of ten SnapTone slots: the first five are AMP models and the next five are DIST models. This adds a selectable SnapTone model; it does not flash firmware."]="Un CLO GP-200 se carga en uno de los diez slots SnapTone: los cinco primeros son modelos AMP y los cinco siguientes, DIST. Añade un modelo SnapTone seleccionable; no flashea el firmware.",
        ["Momentary (400 ms)"]="Momentánea (400 ms)",["Integrated (gated)"]="Integrada (con puertas)",["Sample peak"]="Pico de muestra",
        ["Select an audio input and start measurement."]="Selecciona una entrada de audio e inicia la medición.",
        ["Local BS.1770 measurement. Compare presets through the same input and gain; this does not change preset levels."]="Medición local BS.1770. Compara presets con la misma entrada y ganancia; no cambia sus niveles.",
        ["LUFS measurement supports mono or stereo input only."]="La medición LUFS solo admite entradas mono o estéreo.",
        ["Measuring locally; play audio through the selected input."]="Medición local; reproduce audio por la entrada seleccionada.",
        ["Measurement stopped. Start again to reset and measure a new run."]="Medición detenida. Inicia de nuevo para reiniciar y medir otra sesión.",
        ["Export selected ZIP"]="Exportar selección ZIP",["Toggle selected favorites"]="Alternar favoritos seleccionados",
        ["Add selected to song"]="Añadir selección a canción",["Remove selected from setlist"]="Quitar selección de lista",
        ["Presets added to setlist: "]="Presets añadidos a la lista: ",["Presets removed from setlist: "]="Presets quitados de la lista: ",
        ["Select presets to export."]="Selecciona presets para exportar.",["Presets exported to ZIP: "]="Presets exportados a ZIP: ",
        ["Export ZIP"]="Exportar ZIP",
        ["Duplicate as local copy…"]="Duplicar como copia local…",["Duplicate preset"]="Duplicar preset",
        ["Choose a new filename to preserve existing files."]="Elige un nombre nuevo para conservar los archivos existentes.",
        ["Local copy created: "]="Copia local creada: ",
        ["Move preset"]="Mover preset",["Swap preset slots"]="Intercambiar slots",
        ["Move to slot…"]="Mover a slot…",["Swap with slot…"]="Intercambiar con slot…",
        ["Connect the GP-200 and select a device memory slot first."]="Conecta la GP-200 y selecciona primero un slot del pedal.",
        ["Connect the GP-200 to create a full device backup."]="Conecta la GP-200 para crear un respaldo completo.",
        ["Connect the GP-200 to read the device preset bank."]="Conecta la GP-200 para leer el banco de presets del pedal.",
        ["Temporary device read failure; retaining the connection for retry ("]="Fallo temporal al leer el pedal; se conserva la conexión para reintentar (",
        ["Save or discard the active preset's pending edits before moving or swapping slots."]="Guarda o descarta los cambios pendientes del preset activo antes de mover o intercambiar slots.",
        ["Preset moved. Backup: "]="Preset movido. Copia previa: ",["Preset slots swapped. Backup: "]="Slots intercambiados. Copia previa: ",
        ["Destination preset"]="Preset de destino",
        ["Full Bank ZIP"]="ZIP de banco completo",["The complete set of 256 device presets could not be read."]="No se pudieron leer los 256 presets del pedal.",
        ["The selected archive contains duplicate target slots."]="El archivo contiene slots de destino duplicados.",
        ["Full 256-preset backup exported: "]="Respaldo completo de 256 presets exportado: ",
        ["Full Backup"]="Respaldo total",
        ["Presets GP-200 and ZIP archives"]="Presets GP-200 y archivos ZIP",["GP-200 presets and ZIP archives"]="Presets GP-200 y archivos ZIP",
        ["GP-200 presets and Scenes"]="Presets GP-200 y escenas",["ZIP archives (*.zip)"]="Archivos ZIP (*.zip)",
        ["Custom preset with Scenes"]="Preset Custom con escenas",["GP-200 preset (*.prst)"]="Preset GP-200 (*.prst)",
        ["ZIP archive contains no GP-200 presets."]="El ZIP no contiene presets GP-200.",
        ["ZIP archive contains too many presets."]="El ZIP contiene demasiados presets.",
        ["ZIP archive contains duplicate preset entries."]="El ZIP contiene presets duplicados.",
        ["ZIP archive contains an invalid preset size."]="El ZIP contiene un preset de tamaño no válido.",
        ["ZIP archive is too large."]="El archivo ZIP es demasiado grande.",
        ["ZIP entry expands beyond the preset size limit."]="Una entrada ZIP supera el tamaño máximo.",
        ["ZIP preset size does not match its directory."]="El tamaño del preset no coincide con el índice ZIP.",
        ["ZIP archive contains an invalid or duplicate preset name."]="El ZIP contiene un nombre de preset no válido o duplicado."
    };
    const string Pairs="""
Banda {0} · Hz|Band {0} · Hz
Banda {0} · dB|Band {0} · dB
Banda {0} · Q|Band {0} · Q
Low Cut (19 = extremo mínimo)|Low Cut (19 = minimum)
High Cut (20001 = extremo máximo)|High Cut (20001 = maximum)
Pulsa Leer GP-200 para cargar los ajustes actuales.|Click Read GP-200 to load the current settings.
Pulsa Leer 256 presets. Puedes abrir copias sin cambiar el preset activo de la GP-200.|Click Read 256 presets. You can open copies without changing the active GP-200 preset.
Abre tus presets para explorar bloques, parámetros y asignaciones.|Open presets to explore blocks, parameters and assignments.
Manual local del equipo, documentación de la réplica e inspección de archivos de firmware.|Local device manual, replica documentation and firmware file inspection.
Las casillas eligen los bloques de la asignación CTRL; no son sus interruptores de activación.|Checkboxes select blocks for the CTRL assignment; they do not enable or disable the blocks.
Código de efecto no catalogado: 0x{0}|Uncatalogued effect code: 0x{0}
El pedal contiene este código, pero no hay metadatos de parámetros validados. Se conservan los valores originales y no se exponen controles de edición.|The pedal contains this code, but no parameter metadata has been validated. Original values are preserved and editing controls are not exposed.
Valores sin interpretar: |Uninterpreted values: 
Arrastra un bloque en la cadena superior hasta otro bloque o hueco. También puedes seleccionarlo y usar estas flechas.|Drag a block in the upper chain to another block or position. You can also select it and use these arrows.
Quick Access · tres mandos|Quick Access · three controls
Nombre|Name
Autor|Author
Volumen|Volume
Estilo|Style
Notas|Notes
Guardar|Save
Guardar activo|Save active
Guardar en GP-200|Save to GP-200
Cancelar|Cancel
Deshacer|Undo
Rehacer|Redo
Copiar|Copy
Copiar preset|Copy preset
Pegar…|Paste…
Pegar preset aquí…|Paste preset here…
Recargar|Refresh
Renombrar|Rename
Renombrar slot|Rename slot
Renombrar tipo|Rename type
Exportar…|Export…
Exportar copia…|Export copy…
Exportar preset…|Export preset…
Exportar biblioteca…|Export library…
Importar…|Import…
Importar PRST…|Import PRST…
Importar WAV al slot…|Import WAV to slot…
Abrir presets|Open presets
Abrir copia local|Open local copy
Abrir copia editable|Open editable copy
Abrir manual|Open manual
Bancos y presets|Banks and presets
Banco leído.|Bank loaded.
Tu preset|Your preset
Tipos de preset|Preset types
Leer tipos|Read types
Leer slots|Read slots
Leer GP-200|Read GP-200
Leer 256 presets|Read 256 presets
Leer banco IR|Read IR bank
Leer banco seleccionado|Read selected bank
Seleccionar · edición|Select · edit
Buscar algoritmo|Search algorithms
Buscar por nombre o posición|Search by name or position
Cambiar algoritmo y cargar valores iniciales|Change algorithm and load default values
Cambiar también al CAB recomendado por el AMP|Also select the CAB recommended for the AMP
Bloque activado|Block enabled
Aplicar parámetros|Apply parameters
Acceso rápido|Quick access
Asignaciones CTRL|CTRL assignments
Expresión|Expression
Función|Function
Rango|Range
Para {0}|For {0}
Módulo {0}|Module {0}
Acceso {0}|Access {0}
{0} · parámetro {1}|{0} · parameter {1}
{0} · destino {1}|{0} · destination {1}
Orden de bloques|Block order
← Mover bloque|← Move block
Mover bloque →|Move block →
Mover send|Move send
Mover return|Move return
Nodos de FX loop|FX loop nodes
Arrastra este nodo a otro hueco de la cadena.|Drag this node to another position in the chain.
{0} · {1}\nArrastra el bloque a otra posición.|{0} · {1}\nDrag the block to another position.
CADENA · arrastra para reordenar · interruptor para activar|CHAIN · drag to reorder · switch to enable
Cambios locales|Local changes
Cambios directos al preset activo. Guardar en GP-200 conserva los cambios.|Changes apply directly to the active preset. Save to GP-200 stores them permanently.
Preset activo · cambios directos, guardar es una acción separada.|Active preset · immediate changes; save separately.
Entrada MIDI|MIDI input
Salida MIDI|MIDI output
Reloj MIDI|MIDI clock
Lista MIDI|MIDI chart
Ritmos|Drums
Patrón|Pattern
Tempo BPM|Tempo BPM
Ayuda · preferencias|Help · preferences
Ayuda, preferencias e información|Help, preferences and information
Manual y preferencias…|Manual and preferences…
Escala de interfaz:|Interface scale:
Idioma:|Language:
Idioma guardado. Reinicia la aplicación para aplicarlo.|Language saved. Restart the application to apply it.
Elegir manual…|Choose manual…
Informe de progreso|Progress report
Fuentes y evidencias|Sources and evidence
Guardar informe…|Save report…
Guardar evidencia…|Save evidence…
Inspeccionar firmware BIN…|Inspect firmware BIN…
Ver firmware aportado|View supplied firmware
Archivo WAV|WAV file
Seleccionar WAV…|Select WAV…
Gestionar slots…|Manage slots…
Ningún WAV seleccionado|No WAV selected
Selecciona archivo, slot y nombre.|Select a file, slot and name.
Selecciona un WAV y su posición de destino.|Select a WAV and its destination slot.
Comprobar carga|Check upload
Importando IR…|Uploading IR…
IR Name · máximo 16 caracteres|IR Name · 16 characters maximum
20 slots User IR · 1024 muestras|20 User IR slots · 1024 samples
Eliminar contenido del slot…|Delete slot contents…
Nombre (ASCII, máximo 16)|Name (ASCII, 16 maximum)
Nombre verificado en el catálogo.|Name verified in the catalog.
Nombre verificado mediante lectura del banco IR.|Name verified by reading the IR bank.
Slot IR vacío confirmado mediante lectura del catálogo.|Empty IR slot confirmed by catalog readback.
Importación confirmada · USER IR {0} · {1} muestras.|Upload confirmed · USER IR {0} · {1} samples.
Recibidos {0}/20 slots. Estado leído del dispositivo.|Received {0}/20 slots. State read from the device.
Recibidos {0}/10 slots.|Received {0}/10 slots.
Conectar · edición en vivo|Connect · live editing
Desconectar edición en vivo|Disconnect live editing
Comunicando con GP-200…|Communicating with GP-200…
Conectando y leyendo el preset activo…|Connecting and reading the active preset…
Enviando edición en tiempo real…|Sending live changes…
Enviando y verificando edición en GP-200…|Sending and verifying GP-200 changes…
Cargando biblioteca GP-200 · {0}/256|Loading GP-200 library · {0}/256
Biblioteca GP-200 cargada · 64 bancos / 256 presets.|GP-200 library loaded · 64 banks / 256 presets.
GP200 Replica · Editor local|GP200 Replica · Local editor
GP200 Replica · GP-200 conectada|GP200 Replica · GP-200 connected
GP-200 · Estado actualizado desde la pedalera.|GP-200 · State updated from the device.
GP-200 · Edición confirmada por lectura. Pendiente de guardar en memoria.|GP-200 · Changes verified by readback. Save to store permanently.
GP-200 · Edición confirmada por lectura del preset activo. Sin guardar en memoria.|GP-200 · Changes verified by active preset readback. Not stored permanently.
Espera a que termine la edición pendiente.|Wait for the pending edit to finish.
Espera a que termine la lectura o verificación en curso.|Wait for the current read or verification to finish.
Conecta la GP-200 antes de pegar.|Connect the GP-200 before pasting.
Conecta la GP-200 para seleccionar esta posición.|Connect the GP-200 to select this position.
Copia primero un preset y elige una posición de la GP-200.|Copy a preset first, then select a GP-200 destination.
Copiar → destino|Copy → destination
Mover → destino|Move → destination
Intercambiar ↔ destino|Swap ↔ destination
Copiar banco → destino|Copy bank → destination
Destino:|Destination:
Guardar activo → destino|Save active → destination
Vaciar preset…|Clear preset…
Cancelar lectura|Cancel reading
64 bancos · 4 presets por banco|64 banks · 4 presets per bank
20 tipos compartidos entre presets|20 types shared across presets
256 presets leídos. No se ha cambiado el sonido activo.|256 presets read. Active sound unchanged.
Leyendo {0}/256 · {1}%|Reading {0}/256 · {1}%
Importando {0}/{1}|Importing {0}/{1}
Copiando banco {0}/4|Copying bank {0}/4
Copia abierta para edición local.|Copy opened for local editing.
Copia exportada: {0}|Copy exported: {0}
{0} presets abiertos. Los originales se conservan.|{0} presets opened. Original files preserved.
{0} presets exportados en {1}|{0} presets exported to {1}
Introduce un valor entre {0} y {1}.|Enter a value between {0} and {1}.
Curva orientativa · arrastra los puntos; Q y Master Volume se ajustan debajo.|Illustrative curve · drag the points; adjust Q and Master Volume below.
Current State · lectura no disponible|Current State · readback unavailable
Valores conservados · avanzado|Preserved values · advanced
Detalles del archivo|File details
Un espacio para tu sonido|A space for your sound
TU SONIDO, A TU MANERA|YOUR SOUND, YOUR WAY
Editor independiente · interfaz basada en los flujos del editor oficial.|Independent editor · interface based on the official editor workflows.
GP-200 · Ayuda y preferencias|GP-200 · Help and preferences
GP-200 · Banco User IR|GP-200 · User IR bank
GP-200 · Bancos y presets|GP-200 · Banks and presets
GP-200 · Tipos de preset|GP-200 · Preset types
GP-200 · Slots SnapTone existentes|GP-200 · Existing SnapTone slots
GP200 · Ajustes, EQ, footswitch y ritmos|GP200 · Settings, EQ, footswitch and drums
Omni recibe en todos los canales. DIN Only, USB Only y External usan reloj externo; en esos modos TAP Tempo no controla el tempo. Clock Out habilita la salida de reloj del equipo.|Omni receives on all channels. DIN Only, USB Only and External use external clock; TAP Tempo does not control tempo in those modes. Clock Out enables the device clock output.
""";
}
public static class UiDialogs
{
    public static MessageBoxResult Show(string text,string caption,MessageBoxButton buttons=MessageBoxButton.OK,MessageBoxImage icon=MessageBoxImage.None)=>MessageBox.Show(UiText.T(text),UiText.T(caption),buttons,icon);
    public static MessageBoxResult Show(Window owner,string text,string caption,MessageBoxButton buttons=MessageBoxButton.OK,MessageBoxImage icon=MessageBoxImage.None)=>MessageBox.Show(owner,UiText.T(text),UiText.T(caption),buttons,icon);
}
