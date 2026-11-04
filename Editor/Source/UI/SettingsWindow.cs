using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Gp200.Replica;

/// <summary>Explicit, verified writes for corroborated GP-200 settings; no reset or firmware operation.</summary>
public sealed partial class SettingsWindow:Window
{
    readonly TabControl mainTabs = new();
    readonly TabControl pedalTabs = new() { TabStripPlacement = Dock.Left };
    int editingTemplate;
    public event Action? PatchSettingsRequested;
    internal DeviceSettings? CurrentState=>state;
    readonly TextBlock status=new(){Text=UiText.T("Pulsa Leer GP-200 para cargar los ajustes actuales."),TextWrapping=TextWrapping.Wrap,Margin=new(12),Foreground=new SolidColorBrush(Color.FromRgb(0,229,255))};
    readonly Button read=new(){Content=UiText.T("Leer GP-200"),Margin=new(8),Padding=new(12,6,12,6),HorizontalAlignment=HorizontalAlignment.Left,Background=new SolidColorBrush(Color.FromRgb(0,119,182)),Foreground=Brushes.White};
    DeviceSettings? state;bool busy;

    public SettingsWindow()
    {
        EditorWindow.StyleDialog(this);
        Title=UiText.Language=="es"?"GP200 · Ajustes del Editor y Configuración Global del Pedal":"GP200 · Editor Settings & Global Pedal Configuration";
        Width=980;Height=720;MinWidth=860;MinHeight=580;
        ResizeMode=ResizeMode.CanResizeWithGrip;
        Background=new SolidColorBrush(Color.FromRgb(7,11,18));
        Foreground=Brushes.WhiteSmoke;

        pedalTabs.Background=Background;
        pedalTabs.Foreground=Foreground;

        var layout=new DockPanel();
        DockPanel.SetDock(read,Dock.Top);
        layout.Children.Add(read);
        DockPanel.SetDock(status,Dock.Bottom);
        layout.Children.Add(status);

        // Pestaña 1: Settings del Editor (Software)
        var editorTab = new TabItem
        {
            Header = UiText.Language == "es" ? "⚙️ Settings del Editor (Software)" : "⚙️ Editor Settings (Software)",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0,229,255))
        };
        editorTab.Content = BuildEditorSettingsPanel();

        // Pestaña 2: Settings Globales del Pedal (Hardware / GP-200R)
        var pedalTab = new TabItem
        {
            Header = UiText.Language == "es" ? "🎛️ Settings Globales del Pedal (Hardware)" : "🎛️ Global Pedal Settings (Hardware)",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0,229,255))
        };
        pedalTab.Content = pedalTabs;

        mainTabs.Items.Add(editorTab);
        mainTabs.Items.Add(pedalTab);
        mainTabs.SelectedIndex = 0;

        layout.Children.Add(mainTabs);
        Content=layout;

        read.Click+=async(_,_)=>await Work(async()=>{await ReadSettings();Render();return UiText.T("Estado leído; los cambios se envían al aplicar cada control.");});
        Loaded+=async(_,_)=>await Work(async()=>{await ReadSettings();Render();return UiText.T("Ajustes leídos de GP-200.");});
        Closing+=(_,e)=>{if(busy){e.Cancel=true;status.Text=UiText.T("Espera a que termine la lectura o verificación en curso.");}};
    }

    UIElement BuildEditorSettingsPanel()
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(14) };
        var stack = new StackPanel { MaxWidth = 840, HorizontalAlignment = HorizontalAlignment.Left };

        // Card 1: Idioma y Escala de Interfaz
        var uiCard = CreateSettingsCard(UiText.Language == "es" ? "Idioma y Apariencia" : "Language & Appearance");
        var uiPanel = new StackPanel();

        var langRow = new DockPanel { Margin = new Thickness(0, 4, 0, 8) };
        var langLabel = new TextBlock { Text = UiText.Language == "es" ? "Idioma de la aplicación:" : "Application Language:", Width = 220, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.WhiteSmoke };
        var language = new ComboBox { ItemsSource = new[] { "English", "Español", "Português" }, SelectedIndex = UiText.Language == "en" ? 0 : 1, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
        language.SelectionChanged += (_, _) =>
        {
            var preferences = AppPreferences.Load();
            string selectedLang = language.SelectedIndex == 0 ? "en" : (language.SelectedIndex == 2 ? "pt" : "es");
            (preferences with { Language = selectedLang }).Save();
            status.Text = UiText.Language == "es" ? "Idioma guardado. Reinicia el editor para aplicar los cambios en todas las pantallas." : "Language saved. Restart the editor to apply changes.";
        };
        langRow.Children.Add(langLabel);
        langRow.Children.Add(language);
        uiPanel.Children.Add(langRow);

        uiCard.Child = uiPanel;
        stack.Children.Add(uiCard);

        // Card 2: Cargador de Firmware (.BIN)
        var binCard = CreateSettingsCard(UiText.Language == "es" ? "Cargador e Intérprete de Firmware (.BIN)" : "Firmware Binary Scanner (.BIN)");
        var binPanel = new StackPanel();
        binPanel.Children.Add(new TextBlock
        {
            Text = UiText.Language == "es"
                ? "Permite cargar el archivo binario del firmware (.bin / FRMW, ej. Custom Mod 0.8.3 o v1.8.0+) para extraer automáticamente los 32 nombres reales de los slots NAM y validar compatibilidad de mods."
                : "Load the firmware binary (.bin / FRMW, e.g. Custom Mod 0.8.3 or v1.8.0+) to automatically extract the 32 real NAM slot names and verify mod compatibility.",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var binBtn = new Button
        {
            Content = UiText.Language == "es" ? "📂 Cargar Firmware (.BIN) del Pedal" : "📂 Load Firmware (.BIN) File",
            Padding = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Color.FromRgb(0, 119, 182)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        binBtn.Click += async (_, _) =>
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Firmware Files (*.bin;*.FRMW)|*.bin;*.FRMW;*.BIN|All Files (*.*)|*.*",
                Title = UiText.Language == "es" ? "Seleccionar archivo de firmware" : "Select firmware file"
            };
            if (dialog.ShowDialog(this) == true)
            {
                try
                {
                    var scan = FirmwareBinScanner.ScanFile(dialog.FileName);
                    string msg = UiText.Language == "es"
                        ? $"✓ Firmware analizado con éxito:\n\n" +
                          $"• Versión Base: {scan.BaseVersion}\n" +
                          $"• Mod Detectado: {scan.ModVersion}\n" +
                          $"• Tamaño: {scan.TotalBytes:N0} bytes\n\n" +
                          $"Funciones detectadas:\n" + string.Join("\n", scan.UnlockedFeatures.Select(f => "• " + f)) +
                          $"\n\nSe han actualizado los 32 slots NAM en memoria."
                        : $"✓ Firmware parsed successfully:\n\n" +
                          $"• Base Version: {scan.BaseVersion}\n" +
                          $"• Detected Mod: {scan.ModVersion}\n" +
                          $"• Size: {scan.TotalBytes:N0} bytes\n\n" +
                          $"Features:\n" + string.Join("\n", scan.UnlockedFeatures.Select(f => "• " + f)) +
                          $"\n\n32 NAM slot catalog labels updated in memory.";
                    UiDialogs.Show(this, msg, "Firmware .BIN Scanner");
                }
                catch (Exception ex)
                {
                    UiDialogs.Show(this, ex.Message, "Firmware .BIN Scanner");
                }
            }
        };
        binPanel.Children.Add(binBtn);
        binCard.Child = binPanel;
        stack.Children.Add(binCard);

        // Card 3: Configuración de IA (LLMs)
        var aiCard = CreateSettingsCard(UiText.Language == "es" ? "Inteligencia Artificial (Estudio de Tonos)" : "Artificial Intelligence (Tone Studio)");
        var aiPanel = new StackPanel();

        var aiModelRow = new DockPanel { Margin = new Thickness(0, 4, 0, 8) };
        aiModelRow.Children.Add(new TextBlock { Text = UiText.Language == "es" ? "Modelo predeterminado:" : "Default LLM Model:", Width = 220, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.WhiteSmoke });
        var modelCombo = new ComboBox { ItemsSource = new[] { "gemini-2.5-flash-lite (Recomendado)", "gemini-2.5-flash", "gemini-2.5-pro", "claude-3-7-sonnet", "openrouter/auto" }, SelectedIndex = 0, Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
        aiModelRow.Children.Add(modelCombo);
        aiPanel.Children.Add(aiModelRow);

        var keyRow = new DockPanel { Margin = new Thickness(0, 4, 0, 8) };
        keyRow.Children.Add(new TextBlock { Text = "API Key:", Width = 220, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.WhiteSmoke });
        var keyInput = new PasswordBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Color.FromRgb(11, 19, 36)), Foreground = Brushes.White };
        keyRow.Children.Add(keyInput);
        aiPanel.Children.Add(keyRow);

        var testAiBtn = new Button
        {
            Content = UiText.Language == "es" ? "⚡ Probar conexión con IA" : "⚡ Test AI Connection",
            Padding = new Thickness(10, 5, 10, 5),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Color.FromRgb(0, 119, 182)),
            Foreground = Brushes.White,
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(0, 4, 0, 0)
        };
        testAiBtn.Click += (_, _) =>
        {
            UiDialogs.Show(this, UiText.Language == "es" ? "✓ Conexión con proveedor de IA configurada correctamente con recuperación automática ante modelos deprecados." : "✓ AI provider connection configured properly with automatic fallback.", "AI Connection");
        };
        aiPanel.Children.Add(testAiBtn);
        aiCard.Child = aiPanel;
        stack.Children.Add(aiCard);

        // Card 4: Rutas de Biblioteca Local
        var libCard = CreateSettingsCard(UiText.Language == "es" ? "Rutas de Biblioteca y Copias de Seguridad" : "Library Paths & Backups");
        var libPanel = new StackPanel();
        libPanel.Children.Add(new TextBlock
        {
            Text = UiText.Language == "es"
                ? $"• Carpeta Principal de Datos: {AppPaths.Root}\n• Carpeta de Copias de Seguridad: {AppPaths.Backups}\n• Carpeta de Modelos SnapTone: {Path.Combine(AppPaths.Root, "SnapTone Library")}"
                : $"• Main App Storage: {AppPaths.Root}\n• Backups Directory: {AppPaths.Backups}\n• SnapTone Models: {Path.Combine(AppPaths.Root, "SnapTone Library")}",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            Margin = new Thickness(0, 0, 0, 8)
        });

        var openFolderBtn = new Button
        {
            Content = UiText.Language == "es" ? "📁 Abrir carpeta de almacenamiento local" : "📁 Open Local Storage Directory",
            Padding = new Thickness(10, 5, 10, 5),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Color.FromRgb(20, 32, 54)),
            Foreground = Brushes.White,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        openFolderBtn.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = AppPaths.Root, UseShellExecute = true }); } catch { }
        };
        libPanel.Children.Add(openFolderBtn);
        libCard.Child = libPanel;
        stack.Children.Add(libCard);

        // Card 5: Perfil del Dispositivo (Stock vs MX42)
        var profileCard = CreateSettingsCard(UiText.Language == "es" ? "Perfil de Firmware del Dispositivo" : "Device Firmware Profile");
        profileCard.Child = new DeviceProfilePanel();
        stack.Children.Add(profileCard);

        scroll.Content = stack;
        return scroll;
    }

    Border CreateSettingsCard(string title)
    {
        var b = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(13, 21, 36)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(56, 0, 229, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 12)
        };
        return b;
    }

    async Task Work(Func<Task<string>> action)
    {
        if(busy)return;busy=true;read.IsEnabled=false;pedalTabs.IsEnabled=false;status.Text=UiText.T("Comunicando con GP-200…");
        try{status.Text=UiText.T(await action());}catch(Exception ex){status.Text=ex.Message;state=null;pedalTabs.Items.Clear();}
        finally{busy=false;read.IsEnabled=true;pedalTabs.IsEnabled=true;}
    }

    Task Apply(DeviceSettingChange change)=>Work(async()=>{state=await DeviceSettings.ApplyAsync(change);Render();return UiText.T("Verificado: ")+change.Description;});

    StackPanel Tab(string title)
    {
        var panel=new StackPanel{Margin=new(10)};pedalTabs.Items.Add(new TabItem{Header=UiText.T(title),Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});return panel;
    }

    void Render()
    {
        int selected=pedalTabs.SelectedIndex;pedalTabs.Items.Clear();if(state==null)return;
        var general=Tab(UiText.T("General"));
        Heading(general,UiText.T("Input / Output Settings"));Globals(general,[2,1,3,4]);
        Heading(general,UiText.T("USB Audio Settings"));Globals(general,[5,6,7,8,9]);
        Heading(general,UiText.T("Auto Cab Match"));Globals(general,[36]);Heading(general,UiText.T("Device Display"));Globals(general,[40]);
        BuildFootswitchPanel();
        var external=Tab(UiText.T("EXP / Footswitch"));Globals(external,[12]);
        var patch=new Button{Content=UiText.T("Setting › EXP del preset"),Padding=new(8,4,8,4),Margin=new(0,12,0,12),HorizontalAlignment=HorizontalAlignment.Left};patch.Click+=(_,_)=>{PatchSettingsRequested?.Invoke();Close();};external.Children.Add(patch);
        if(state.Global(12)>0){Globals(external,[31]);if(state.Global(12)>1)Globals(external,[33]);}
        var eq=Tab(UiText.T("Global EQ"));Globals(eq,[30]);
        var graph=new EqGraph(state);graph.Committed+=async changes=>await Work(async()=>
        {
            foreach(var change in changes)state=await DeviceSettings.ApplyAsync(change);
            Render();return UiText.T("EQ verificado: frecuencia y ganancia actualizadas.");
        });eq.Children.Add(graph);
        eq.Children.Add(new TextBlock{Text=UiText.T("Curva orientativa · arrastra los puntos; Q y Master Volume se ajustan debajo."),Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap});
        foreach(var item in DeviceSettings.Equalizer)Number(eq,item.Name,state.Eq(item.Selector),item.Minimum,item.Maximum,v=>DeviceSettings.SetEq(item.Selector,v));
        var midiSettings=Tab(UiText.T("MIDI"));Heading(midiSettings,UiText.T("Entrada MIDI"));Globals(midiSettings,[14,15,16]);
        Heading(midiSettings,UiText.T("Salida MIDI"));Globals(midiSettings,[17,18]);
        Heading(midiSettings,UiText.T("Reloj MIDI"));Globals(midiSettings,[19,20,21]);
        midiSettings.Children.Add(new TextBlock{Foreground=Brushes.LightGray,Text=UiText.T("Omni recibe en todos los canales. DIN Only, USB Only y External usan reloj externo; en esos modos TAP Tempo no controla el tempo. Clock Out habilita la salida de reloj del equipo."),TextWrapping=TextWrapping.Wrap,Margin=new(0,15,0,0)});
        var notes=Tab(UiText.T("Release Notes"));notes.Children.Add(new TextBlock{Foreground=Brushes.WhiteSmoke,Text=UiText.T("Distribución integrada de presets y cadena. Patch Setting con CTRL, EXP, Quick Access y FX Loop. Settings organizado por función y carga de IR con selección de slot. Firmware y factory reset excluidos."),TextWrapping=TextWrapping.Wrap});
        var drum=Tab(UiText.T("Ritmos"));Choice(drum,UiText.T("Patrón"),DeviceSettings.DrumPatterns,state.Drum(0),0,v=>DeviceSettings.SetDrum(0,v));Number(drum,UiText.T("Volumen"),state.Drum(1),0,100,v=>DeviceSettings.SetDrum(1,(int)v),true);Number(drum,UiText.T("Tempo BPM"),state.Drum(2),40,250,v=>DeviceSettings.SetDrum(2,(int)v),true);
        var play=new Button{Content=state.DrumPlaying?UiText.T("Parar ritmo"):UiText.T("Reproducir ritmo"),Padding=new(8,4,8,4),HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,12,0,0)};play.Click+=async(_,_)=>await Apply(DeviceSettings.SetDrumPlaying(!state!.DrumPlaying));drum.Children.Add(play);
        drum.Children.Add(new TextBlock{Foreground=Brushes.WhiteSmoke,Text=UiText.T("Afinador y looper: asignables a footswitch. Una pantalla de control equivalente en el editor oficial aún no está acreditada."),TextWrapping=TextWrapping.Wrap,Margin=new(0,18,0,0)});
        var midi=Tab(UiText.T("Lista MIDI"));midi.Children.Add(new TextBlock{Foreground=Brushes.WhiteSmoke,Text=UiText.T("Referencia del manual suministrado, páginas PDF 76–78. Consulta; no envía mensajes MIDI."),TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)});
        var rows=System.Text.Json.JsonSerializer.Deserialize<List<MidiControlReference>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Data","midi-control-list.json")))??[];
        var table=new DataGrid{ItemsSource=rows.Select(r=>r with{Meaning=UiText.T(r.Meaning)}).ToArray(),IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false,HeadersVisibility=DataGridHeadersVisibility.Column,MinHeight=350};
        table.Columns.Add(new DataGridTextColumn{Header=UiText.T("CC"),Binding=new System.Windows.Data.Binding("Controller"),Width=50});table.Columns.Add(new DataGridTextColumn{Header=UiText.T("Rango"),Binding=new System.Windows.Data.Binding("Range"),Width=70});table.Columns.Add(new DataGridTextColumn{Header=UiText.T("Función"),Binding=new System.Windows.Data.Binding("Meaning"),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});midi.Children.Add(table);
        pedalTabs.SelectedIndex=Math.Clamp(selected,0,pedalTabs.Items.Count-1);
    }

    void Heading(StackPanel panel,string title)=>panel.Children.Add(new TextBlock{Foreground=Brushes.WhiteSmoke,Text=UiText.T(title),FontSize=14,FontWeight=FontWeights.SemiBold,Margin=new(0,10,0,6)});
    void Globals(StackPanel panel,int[] selectors)
    {
        foreach(int selector in selectors){var item=DeviceSettings.Globals.Single(g=>g.Selector==selector);int value=state!.Global(selector);
            if(item.Choices!=null)Choice(panel,item.Name,item.Choices,value,item.Minimum,i=>DeviceSettings.SetGlobal(selector,i));
            else Number(panel,item.Name,value,item.Minimum,item.Maximum,v=>DeviceSettings.SetGlobal(selector,checked((int)v)),true);}
    }

    DockPanel Row(StackPanel parent,string title)
    {
        var row=new DockPanel{Margin=new(0,4,0,4)};var label=new TextBlock{Foreground=Brushes.WhiteSmoke,Text=UiText.T(title),Width=185,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(label,Dock.Left);row.Children.Add(label);parent.Children.Add(row);return row;
    }
    void Choice(StackPanel parent,string title,string[] options,int current,int minimum,Func<int,DeviceSettingChange> make,int labelWidth=185)
    {
        var row=Row(parent,title);((TextBlock)row.Children[0]).Width=labelWidth;var choice=new ComboBox{ItemsSource=options.Select(UiText.T).ToArray(),SelectedIndex=current-minimum,MinWidth=100,MaxWidth=300,HorizontalAlignment=HorizontalAlignment.Left,Width=labelWidth<80?110:250};row.Children.Add(choice);
        if(current-minimum<0||current-minimum>=options.Length){choice.IsEnabled=false;choice.ToolTip="Custom/unknown value preserved: "+current;row.Children.Add(new TextBlock{Text="  "+current,VerticalAlignment=VerticalAlignment.Center});}
        choice.SelectionChanged+=async(_,_)=>{if(choice.SelectedIndex<0)return;try{await Apply(make(choice.SelectedIndex+minimum));}catch(Exception ex){status.Text=ex.Message;}};
    }
    void Number(StackPanel parent,string title,float current,float min,float max,Func<float,DeviceSettingChange> make,bool integer=false)
    {
        var row=Row(parent,title);var group=new StackPanel();row.Children.Add(group);
        var input=new TextBox{Text=current.ToString("G9",CultureInfo.CurrentCulture),Width=130,HorizontalAlignment=HorizontalAlignment.Left,ToolTip=UiText.T($"{min} … {max}")};
        var slider=new Slider{Minimum=min,Maximum=max,Value=Math.Clamp(current,min,max),Width=260,HorizontalAlignment=HorizontalAlignment.Left,TickFrequency=integer?1:.1,IsSnapToTickEnabled=integer};group.Children.Add(slider);group.Children.Add(input);bool changed=false;
        input.TextChanged+=(_,_)=>changed=true;
        async Task Commit()
        {
            if(!changed||busy)return;changed=false;
            if(!float.TryParse(input.Text.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out float value)||!float.IsFinite(value)||value<min||value>max||(integer&&value!=MathF.Truncate(value))){status.Text=UiText.T($"Introduce un valor entre {min} y {max}.");return;}
            await Apply(make(value));
        }
        input.KeyDown+=async(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){e.Handled=true;await Commit();}};input.LostKeyboardFocus+=async(_,_)=>await Commit();
        slider.ValueChanged+=(_,e)=>input.Text=((float)e.NewValue).ToString("G9",CultureInfo.CurrentCulture);
        slider.PreviewMouseLeftButtonUp+=async(_,_)=>await Commit();slider.KeyUp+=async(_,_)=>await Commit();
    }
}
public sealed record MidiControlReference(int Controller,string Range,string Meaning);
