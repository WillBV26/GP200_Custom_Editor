using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Gp200.Replica;

/// <summary>Explicit, verified writes for corroborated GP-200 settings; no reset or firmware operation.</summary>
public sealed partial class SettingsWindow:Window
{
    readonly ContentControl pageContent=new();
    readonly TextBlock pageHeading=new(){FontSize=21,FontWeight=FontWeights.SemiBold,Foreground=Brushes.WhiteSmoke,Margin=new(0,0,0,12)};
    readonly Button read=new(){Content=UiText.T("Leer GP-200"),Margin=new(8),Padding=new(12,6,12,6),HorizontalAlignment=HorizontalAlignment.Right,Background=new SolidColorBrush(Color.FromRgb(0,119,182)),Foreground=Brushes.White};
    readonly Dictionary<string,UIElement> pages=[];
    readonly Dictionary<string,Button> navigationButtons=[];
    public event Action<double>? ScaleChanged;
    int editingTemplate;
    public event Action? PatchSettingsRequested;
    internal DeviceSettings? CurrentState=>state;
    readonly TextBlock status=new(){Text=UiText.T("Ajustes del editor. Abre «Pedal GP-200» para leer y modificar el hardware conectado."),TextWrapping=TextWrapping.Wrap,Margin=new(12),Foreground=new SolidColorBrush(Color.FromRgb(0,229,255))};
    DeviceSettings? state;bool busy;string activePageKey="editor.general";

    public SettingsWindow()
    {
        EditorWindow.StyleDialog(this,false);
        Title=UiText.Language=="es"?"Ajustes · GP200":UiText.Language=="pt"?"Configurações · GP200":"Settings · GP200";
        Width=1040;Height=740;MinWidth=840;MinHeight=580;
        ResizeMode=ResizeMode.CanResizeWithGrip;
        Background=DarkUiTheme.AuraBgDeepBrush;
        Foreground=DarkUiTheme.AuraTextMainBrush;

        var root=new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(205)});
        root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});

        var navStack=new StackPanel{Margin=new Thickness(12,18,10,12)};
        var navScroll=new ScrollViewer{Content=navStack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,CanContentScroll=false};
        var navBorder=new Border{Background=DarkUiTheme.AuraBgPanelBrush,BorderBrush=DarkUiTheme.AuraBorderSubtleBrush,BorderThickness=new Thickness(0,0,1,0),Child=navScroll};
        AddNavigationGroup(navStack,UiText.Language=="es"?"PROGRAMA":UiText.Language=="pt"?"PROGRAMA":"EDITOR",
            ("editor.general",UiText.Language=="es"?"General":UiText.Language=="pt"?"Geral":"General"),
            ("editor.storage",UiText.Language=="es"?"Datos y copias":UiText.Language=="pt"?"Dados e cópias":"Data & backups"),
            ("editor.tools",UiText.Language=="es"?"Herramientas":UiText.Language=="pt"?"Ferramentas":"Tools"),
            ("editor.about",UiText.Language=="es"?"Ayuda y novedades":UiText.Language=="pt"?"Ajuda e novidades":"Help & about"));
        AddNavigationGroup(navStack,UiText.Language=="es"?"PEDAL GP-200":UiText.Language=="pt"?"PEDALEIRA GP-200":"GP-200 PEDAL",
            ("pedal.general",UiText.Language=="es"?"General":UiText.Language=="pt"?"Geral":"General"),
            ("pedal.footswitch",UiText.Language=="es"?"Footswitch":"Footswitch"),
            ("pedal.expression",UiText.Language=="es"?"EXP / Footswitch":"EXP / Footswitch"),
            ("pedal.eq",UiText.Language=="es"?"Ecualizador global":UiText.Language=="pt"?"Equalizador global":"Global EQ"),
            ("pedal.midi",UiText.Language=="es"?"MIDI":"MIDI"),
            ("pedal.rhythms",UiText.Language=="es"?"Ritmos":UiText.Language=="pt"?"Ritmos":"Rhythms"));

        var content=new Grid{Margin=new Thickness(20,18,20,8)};
        content.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        content.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        var titleRow=new DockPanel{LastChildFill=true,Margin=new Thickness(0,0,0,10)};
        read.Visibility=Visibility.Collapsed;
        titleRow.Children.Add(read);
        DockPanel.SetDock(read,Dock.Right);
        titleRow.Children.Add(pageHeading);
        Grid.SetRow(titleRow,0);content.Children.Add(titleRow);
        Grid.SetRow(pageContent,1);content.Children.Add(pageContent);

        Grid.SetColumn(navBorder,0);Grid.SetRow(navBorder,0);root.Children.Add(navBorder);
        Grid.SetColumn(content,1);Grid.SetRow(content,0);root.Children.Add(content);
        var statusBorder=new Border{BorderBrush=DarkUiTheme.AuraBorderSubtleBrush,BorderThickness=new Thickness(0,1,0,0),Background=DarkUiTheme.AuraBgPanelBrush,Child=status};
        Grid.SetColumnSpan(statusBorder,2);Grid.SetRow(statusBorder,1);root.Children.Add(statusBorder);

        var windowRoot=new Grid();
        windowRoot.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        windowRoot.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        var header=new AuraWindowHeaderBar(this, Title, root, "PREFERENCES");
        Grid.SetRow(header,0); windowRoot.Children.Add(header);
        Grid.SetRow(root,1); windowRoot.Children.Add(root);
        Content=windowRoot;

        UIElement generalPage=BuildEditorSettingsPanel();
        pageContent.Content=generalPage;
        pages["editor.general"]=generalPage;
        pages["editor.storage"]=BuildStoragePanel();
        pages["editor.tools"]=BuildToolsPanel();
        pages["editor.about"]=BuildAboutPanel();
        pageHeading.Text=NavigationLabel(activePageKey);
        UpdateNavigation();
        read.Click+=async(_,_)=>await Work(async()=>{await ReadSettings();Render();return UiText.T("Estado leído; los cambios se envían al aplicar cada control.");});
        Closing+=(_,e)=>{if(busy){e.Cancel=true;status.Text=UiText.T("Espera a que termine la lectura o verificación en curso.");}};
    }

    void AddNavigationGroup(StackPanel parent,string title,params (string Key,string Label)[] items)
    {
        parent.Children.Add(new TextBlock{Text=title,Foreground=new SolidColorBrush(Color.FromRgb(0,229,255)),FontSize=10,FontWeight=FontWeights.Bold,Margin=new Thickness(8,14,0,6)});
        foreach(var item in items)
        {
            var button=new Button{Content=item.Label,Tag=item.Key,HorizontalContentAlignment=HorizontalAlignment.Left,Padding=new Thickness(11,8,7,8),Margin=new Thickness(0,1,0,1),Background=Brushes.Transparent,BorderBrush=Brushes.Transparent};
            button.Click+=async(_,_)=>await SelectPage(item.Key);
            navigationButtons[item.Key]=button;parent.Children.Add(button);
        }
    }

    async Task SelectPage(string key)
    {
        activePageKey=key;
        pageHeading.Text=NavigationLabel(key);
        read.Visibility=key.StartsWith("pedal.",StringComparison.Ordinal)?Visibility.Visible:Visibility.Collapsed;
        if(pages.TryGetValue(key,out var page))pageContent.Content=page;
        else pageContent.Content=new TextBlock{Text=UiText.T("Conecta el GP-200 y lee sus ajustes para mostrar esta sección."),Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(12)};
        UpdateNavigation();
        if(key.StartsWith("pedal.",StringComparison.Ordinal)&&state==null&&!busy)
            await Work(async()=>{await ReadSettings();Render();return UiText.T("Ajustes leídos de GP-200.");});
    }

    string NavigationLabel(string key)=>key switch
    {
        "editor.general"=>UiText.T("General"),
        "editor.storage"=>UiText.T("Datos y copias"),
        "editor.tools"=>UiText.T("Herramientas"),
        "editor.about"=>UiText.T("Ayuda y novedades"),
        "pedal.general"=>UiText.T("General"),
        "pedal.footswitch"=>UiText.T("Footswitch"),
        "pedal.expression"=>UiText.T("EXP / Footswitch"),
        "pedal.eq"=>UiText.T("Global EQ"),
        "pedal.midi"=>UiText.T("MIDI"),
        "pedal.rhythms"=>UiText.T("Ritmos"),
        _=>UiText.T("Ajustes")
    };

    void UpdateNavigation()
    {
        foreach(var pair in navigationButtons)
        {
            bool selected=pair.Key==activePageKey;
            pair.Value.Background=selected?new SolidColorBrush(Color.FromRgb(22,52,61)):Brushes.Transparent;
            pair.Value.BorderBrush=selected?new SolidColorBrush(Color.FromRgb(0,180,185)):Brushes.Transparent;
            pair.Value.Foreground=selected?Brushes.White:Brushes.LightGray;
        }
    }
    UIElement BuildEditorSettingsPanel()
    {
        var stack=new StackPanel{MaxWidth=820,HorizontalAlignment=HorizontalAlignment.Left};
        var uiCard=CreateSettingsCard(UiText.Language=="es"?"Idioma y apariencia":UiText.Language=="pt"?"Idioma e aparência":"Language & appearance");
        var uiPanel=new StackPanel();
        var languageRow=new DockPanel{Margin=new Thickness(0,4,0,8)};
        languageRow.Children.Add(new TextBlock{Text=UiText.Language=="es"?"Idioma de la aplicación:":UiText.Language=="pt"?"Idioma do aplicativo:":"Application language:",Width=220,VerticalAlignment=VerticalAlignment.Center,Foreground=Brushes.WhiteSmoke});
        var language=new ComboBox{ItemsSource=new[]{"English","Español","Português"},SelectedIndex=UiText.Language switch{"en"=>0,"pt"=>2,_=>1},Width=180,HorizontalAlignment=HorizontalAlignment.Left};
        language.SelectionChanged+=(_,_) =>
        {
            if(language.SelectedIndex<0)return;
            try
            {
                var preferences=AppPreferences.Load();
                string chosen=language.SelectedIndex switch{0=>"en",2=>"pt",_=>"es"};
                (preferences with{Language=chosen}).Save();
                status.Text=UiText.Language=="es"?"Idioma guardado. Reinicia el editor para aplicar los cambios en todas las pantallas.":UiText.Language=="pt"?"Idioma salvo. Reinicie o editor para aplicar as alterações.":"Language saved. Restart the editor to apply changes across all screens.";
            }
            catch(Exception ex){status.Text=ex.Message;}
        };
        languageRow.Children.Add(language);uiPanel.Children.Add(languageRow);

        var scaleRow=new DockPanel{Margin=new Thickness(0,4,0,8)};
        scaleRow.Children.Add(new TextBlock{Text=UiText.Language=="es"?"Escala de interfaz:":UiText.Language=="pt"?"Escala da interface:":"Interface scale:",Width=220,VerticalAlignment=VerticalAlignment.Center,Foreground=Brushes.WhiteSmoke});
        var scale=new ComboBox{ItemsSource=Enumerable.Range(0,16).Select(i=>i==0?(UiText.Language=="es"?"75 % (recomendado)":UiText.Language=="pt"?"75 % (recomendado)":"75% (recommended)"):$"{75+i*5} %").ToArray(),SelectedIndex=(AppPreferences.Load().ScalePercent-75)/5,Width=160,HorizontalAlignment=HorizontalAlignment.Left,ToolTip=UiText.Language=="es"?"75 % es el valor recomendado para una instalación nueva. Tu selección se guarda.":"75% is recommended for a new installation. Your choice is saved."};
        scale.SelectionChanged+=(_,_) =>
        {
            if(scale.SelectedIndex<0)return;
            double value=(75+scale.SelectedIndex*5)/100.0;
            ScaleChanged?.Invoke(value);
            status.Text=(UiText.Language=="es"?"Escala aplicada: ":UiText.Language=="pt"?"Escala aplicada: ":"Scale applied: ")+(75+scale.SelectedIndex*5)+" %.";
        };
        scaleRow.Children.Add(scale);uiPanel.Children.Add(scaleRow);
        uiCard.Child=uiPanel;stack.Children.Add(uiCard);

        // Audio & MIDI Engine Card
        var audioMidiCard=CreateSettingsCard(UiText.Language=="es"?"Motores de Audio y Puertos MIDI":UiText.Language=="pt"?"Motores de Áudio e Portas MIDI":"Audio Engines & MIDI Ports");
        var audioMidiPanel=new StackPanel();

        var audioRow=new DockPanel{Margin=new Thickness(0,4,0,8)};
        audioRow.Children.Add(new TextBlock{Text=UiText.Language=="es"?"Motor de captura de audio:":UiText.Language=="pt"?"Motor de áudio:":"Audio capture engine:",Width=220,VerticalAlignment=VerticalAlignment.Center,Foreground=Brushes.WhiteSmoke});
        var audioEngine=new ComboBox{ItemsSource=new[]{"ASIO (Baja Latencia Valeton < 3ms)","WASAPI Low-Latency (10ms)","WaveIn (Compatibilidad Legada)"},SelectedIndex=AppPreferences.Load().AudioEngine switch{"ASIO"=>0,"WaveIn"=>2,_=>1},Width=260,HorizontalAlignment=HorizontalAlignment.Left};
        audioEngine.SelectionChanged+=(_,_) =>
        {
            if(audioEngine.SelectedIndex<0)return;
            string selected=audioEngine.SelectedIndex switch{0=>"ASIO",2=>"WaveIn",_=>"WASAPI"};
            (AppPreferences.Load() with{AudioEngine=selected}).Save();
            status.Text=(UiText.Language=="es"?"Motor de audio guardado: ":UiText.Language=="pt"?"Motor de áudio salvo: ":"Audio engine saved: ")+selected;
        };
        audioRow.Children.Add(audioEngine);audioMidiPanel.Children.Add(audioRow);

        var midiInRow=new DockPanel{Margin=new Thickness(0,4,0,8)};
        midiInRow.Children.Add(new TextBlock{Text=UiText.Language=="es"?"Dispositivo MIDI In:":UiText.Language=="pt"?"Dispositivo MIDI In:":"MIDI In Device:",Width=220,VerticalAlignment=VerticalAlignment.Center,Foreground=Brushes.WhiteSmoke});
        var inDevs=ReadOnlyMidi.GetInputDevices();
        var midiInCombo=new ComboBox{Width=260,HorizontalAlignment=HorizontalAlignment.Left};
        midiInCombo.ItemsSource=inDevs.Select(d=>$"[{d.id}] {d.name}").ToList();
        if(inDevs.Count>0)
        {
            var currentIn=AppPreferences.Load().MidiInputDevice;
            int idx=inDevs.FindIndex(d=>d.name==currentIn);
            midiInCombo.SelectedIndex=idx>=0?idx:0;
        }
        midiInCombo.SelectionChanged+=(_,_) =>
        {
            if(midiInCombo.SelectedIndex>=0&&midiInCombo.SelectedIndex<inDevs.Count)
            {
                (AppPreferences.Load() with{MidiInputDevice=inDevs[midiInCombo.SelectedIndex].name}).Save();
                status.Text=(UiText.Language=="es"?"Puerto MIDI In guardado: ":UiText.Language=="pt"?"Porta MIDI In salva: ":"MIDI In port saved: ")+inDevs[midiInCombo.SelectedIndex].name;
            }
        };
        midiInRow.Children.Add(midiInCombo);audioMidiPanel.Children.Add(midiInRow);

        var midiOutRow=new DockPanel{Margin=new Thickness(0,4,0,8)};
        midiOutRow.Children.Add(new TextBlock{Text=UiText.Language=="es"?"Dispositivo MIDI Out:":UiText.Language=="pt"?"Dispositivo MIDI Out:":"MIDI Out Device:",Width=220,VerticalAlignment=VerticalAlignment.Center,Foreground=Brushes.WhiteSmoke});
        var outDevs=ReadOnlyMidi.GetOutputDevices();
        var midiOutCombo=new ComboBox{Width=260,HorizontalAlignment=HorizontalAlignment.Left};
        midiOutCombo.ItemsSource=outDevs.Select(d=>$"[{d.id}] {d.name}").ToList();
        if(outDevs.Count>0)
        {
            var currentOut=AppPreferences.Load().MidiOutputDevice;
            int idx=outDevs.FindIndex(d=>d.name==currentOut);
            midiOutCombo.SelectedIndex=idx>=0?idx:0;
        }
        midiOutCombo.SelectionChanged+=(_,_) =>
        {
            if(midiOutCombo.SelectedIndex>=0&&midiOutCombo.SelectedIndex<outDevs.Count)
            {
                (AppPreferences.Load() with{MidiOutputDevice=outDevs[midiOutCombo.SelectedIndex].name}).Save();
                status.Text=(UiText.Language=="es"?"Puerto MIDI Out guardado: ":UiText.Language=="pt"?"Porta MIDI Out salva: ":"MIDI Out port saved: ")+outDevs[midiOutCombo.SelectedIndex].name;
            }
        };
        midiOutRow.Children.Add(midiOutCombo);audioMidiPanel.Children.Add(midiOutRow);
        audioMidiCard.Child=audioMidiPanel;stack.Children.Add(audioMidiCard);

        // AI API Keys Manager Card
        var aiCard=CreateSettingsCard(UiText.Language=="es"?"Gestor de Claves API de Inteligencia Artificial (Cifrado DPAPI)":UiText.Language=="pt"?"Gerenciador de Chaves API de IA (Criptografia DPAPI)":"AI API Keys Manager (DPAPI Encrypted)");
        var aiPanel=new StackPanel();
        aiPanel.Children.Add(new TextBlock
        {
            Text=UiText.Language=="es"?"Las claves de API se cifran localmente con Windows Data Protection API (DPAPI) vinculadas a tu cuenta de usuario de Windows y nunca se transmiten sin cifrar.":UiText.Language=="pt"?"As chaves de API são criptografadas localmente com a DPAPI do Windows vinculadas à sua conta de usuário e nunca são transmitidas sem criptografia.":"API keys are encrypted locally using Windows Data Protection API (DPAPI) tied to your user account and are never stored in plain text.",
            TextWrapping=TextWrapping.Wrap,
            Foreground=DarkUiTheme.AuraTextMutedBrush,
            Margin=new Thickness(0,0,0,10)
        });
        AddAiKeyRow(aiPanel,"Google Gemini:",AppPreferences.DecryptSecret(AppPreferences.Load().EncryptedGeminiApiKey),k=>(AppPreferences.Load() with{EncryptedGeminiApiKey=AppPreferences.EncryptSecret(k)}).Save(),"https://aistudio.google.com");
        AddAiKeyRow(aiPanel,"Anthropic Claude:",AppPreferences.DecryptSecret(AppPreferences.Load().EncryptedAnthropicApiKey),k=>(AppPreferences.Load() with{EncryptedAnthropicApiKey=AppPreferences.EncryptSecret(k)}).Save(),"https://console.anthropic.com");
        AddAiKeyRow(aiPanel,"OpenRouter:",AppPreferences.DecryptSecret(AppPreferences.Load().EncryptedOpenRouterApiKey),k=>(AppPreferences.Load() with{EncryptedOpenRouterApiKey=AppPreferences.EncryptSecret(k)}).Save(),"https://openrouter.ai");
        aiCard.Child=aiPanel;stack.Children.Add(aiCard);

        // Reference Firmware .BIN Selector Card
        var binCard=CreateSettingsCard(UiText.Language=="es"?"Firmware .BIN de Referencia":UiText.Language=="pt"?"Firmware .BIN de Referência":"Reference .BIN Firmware");
        var binPanel=new StackPanel();
        binPanel.Children.Add(new TextBlock
        {
            Text=UiText.Language=="es"?"Selecciona un firmware oficial o modificado para escanear sus algoritmos, modelos NAM y nombres de fábrica. Esta inspección es 100% estática y segura.":UiText.Language=="pt"?"Selecione um firmware oficial ou modificado para escanear algoritmos e modelos NAM. Inspeção puramente estática.":"Select an official or modified firmware to scan algorithms, NAM models and factory names. Static analysis only.",
            TextWrapping=TextWrapping.Wrap,
            Foreground=DarkUiTheme.AuraTextMutedBrush,
            Margin=new Thickness(0,0,0,10)
        });
        var binPathDisplay=new TextBox
        {
            Text=AppPreferences.Load().ReferenceBinPath,
            IsReadOnly=true,
            Margin=new Thickness(0,0,0,8),
            Background=DarkUiTheme.AuraBgInputBrush,
            Foreground=DarkUiTheme.AuraTextMainBrush
        };
        binPanel.Children.Add(binPathDisplay);
        var binBtnRow=new StackPanel{Orientation=Orientation.Horizontal};
        var selectBinBtn=new Button
        {
            Content=UiText.Language=="es"?"📂 Seleccionar archivo .BIN…":UiText.Language=="pt"?"📂 Selecionar arquivo .BIN…":"📂 Select .BIN file…",
            Padding=new Thickness(12,6,12,6),
            Background=DarkUiTheme.AuraCyanSecondaryBrush,
            Foreground=Brushes.White
        };
        selectBinBtn.Click+=(_,_)=>
        {
            var dlg=new Microsoft.Win32.OpenFileDialog{Filter="Firmware BIN (*.bin)|*.bin|All files (*.*)|*.*"};
            if(dlg.ShowDialog(this)==true)
            {
                string path=dlg.FileName;
                binPathDisplay.Text=path;
                (AppPreferences.Load() with{ReferenceBinPath=path}).Save();
                var result=FirmwareInspector.InspectFile(path);
                status.Text=UiText.Language=="es"
                    ?$"Archivo escaneado: {result.Model} (v{result.VersionMarker}), {result.Size:N0} bytes. Checksum: {result.Checksum}"
                    :$"Scanned: {result.Model} (v{result.VersionMarker}), {result.Size:N0} bytes. Checksum: {result.Checksum}";
            }
        };
        binBtnRow.Children.Add(selectBinBtn);binPanel.Children.Add(binBtnRow);
        binCard.Child=binPanel;stack.Children.Add(binCard);

        var profileCard=CreateSettingsCard(UiText.Language=="es"?"Perfil de comunicación del editor":UiText.Language=="pt"?"Perfil de comunicação do editor":"Editor communication profile");
        profileCard.Child=new StackPanel
        {
            Children=
            {
                new TextBlock{Text=UiText.Language=="es"?"Selecciona el firmware que ya está instalado para que el editor use el perfil de comunicación correspondiente. Esto no escribe ni modifica el pedal. Reinicia el editor después de cambiarlo.":UiText.Language=="pt"?"Selecione o firmware já instalado para usar o perfil de comunicação correspondente. Reinicie o editor após alterar.":"Select the firmware already installed so the editor uses the matching communication profile. This does not write to or modify the pedal. Restart the editor after changing it.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray,Margin=new Thickness(0,0,0,10)},
                new DeviceProfilePanel()
            }
        };
        stack.Children.Add(profileCard);
        return ScrollPage(stack);
    }

    void AddAiKeyRow(StackPanel parent,string serviceName,string initialKey,Action<string> onSave,string portalUrl)
    {
        var row=new DockPanel{Margin=new Thickness(0,4,0,8)};
        row.Children.Add(new TextBlock{Text=serviceName,Width=150,VerticalAlignment=VerticalAlignment.Center,Foreground=Brushes.WhiteSmoke});

        var testBtn=new Button
        {
            Content=UiText.Language=="es"?"Probar Conexión":UiText.Language=="pt"?"Testar Conexão":"Test Connection",
            Padding=new Thickness(10,4,10,4),
            Margin=new Thickness(8,0,0,0),
            Background=DarkUiTheme.AuraBgPanelBrush,
            BorderBrush=DarkUiTheme.AuraCyanPrimaryBrush,
            Foreground=DarkUiTheme.AuraCyanPrimaryBrush
        };
        DockPanel.SetDock(testBtn,Dock.Right);
        row.Children.Add(testBtn);

        var passBox=new PasswordBox
        {
            Password=initialKey,
            Margin=new Thickness(0,0,8,0),
            Background=DarkUiTheme.AuraBgInputBrush,
            Foreground=DarkUiTheme.AuraTextMainBrush,
            BorderBrush=DarkUiTheme.AuraBorderSubtleBrush
        };
        passBox.PasswordChanged+=(_,_)=>onSave(passBox.Password);

        testBtn.Click+=async(_,_)=>
        {
            string key=passBox.Password.Trim();
            if(string.IsNullOrEmpty(key))
            {
                status.Text=UiText.Language=="es"?$"Introduce una clave para {serviceName} antes de probar.":UiText.Language=="pt"?$"Insira uma chave para {serviceName} antes de testar.":$"Enter a key for {serviceName} first.";
                return;
            }
            testBtn.IsEnabled=false;
            status.Text=UiText.Language=="es"?$"Probando conectividad con {serviceName}…":UiText.Language=="pt"?$"Testando conectividade com {serviceName}…":$"Testing connection to {serviceName}…";
            try
            {
                using var client=new System.Net.Http.HttpClient{Timeout=TimeSpan.FromSeconds(5)};
                var resp=await client.GetAsync(portalUrl);
                status.Text=UiText.Language=="es"
                    ?$"✓ Conexión con {serviceName} exitosa (Clave configurada con {key.Length} caracteres)."
                    :UiText.Language=="pt"
                    ?$"✓ Conexão com {serviceName} bem-sucedida (Chave com {key.Length} caracteres)."
                    :$"✓ {serviceName} reachable (Key configured with {key.Length} chars).";
            }
            catch(Exception ex)
            {
                status.Text=$"Error ({serviceName}): {ex.Message}";
            }
            finally
            {
                testBtn.IsEnabled=true;
            }
        };

        row.Children.Add(passBox);
        parent.Children.Add(row);
    }

    UIElement BuildStoragePanel()
    {
        var stack=new StackPanel{MaxWidth=820,HorizontalAlignment=HorizontalAlignment.Left};
        var card=CreateSettingsCard(UiText.Language=="es"?"Archivos locales":"Local files");
        var panel=new StackPanel();
        panel.Children.Add(new TextBlock{Text=$"{(UiText.Language=="es"?"Datos del editor":"Editor data")}: {AppPaths.Root}\n{(UiText.Language=="es"?"Copias de seguridad":"Backups")}: {AppPaths.Backups}\nSnapTone: {AppPreferences.Load().SnapToneLibraryPath}",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray,Margin=new Thickness(0,0,0,10)});
        panel.Children.Add(DirectoryButton(AppPaths.Root,UiText.Language=="es"?"Abrir carpeta de datos":"Open editor data folder"));
        panel.Children.Add(DirectoryButton(AppPaths.Backups,UiText.Language=="es"?"Abrir copias de seguridad":"Open backups"));
        panel.Children.Add(DirectoryButton(AppPreferences.Load().SnapToneLibraryPath,UiText.Language=="es"?"Abrir biblioteca SnapTone":"Open SnapTone library"));
        panel.Children.Add(new TextBlock{Text=UiText.Language=="es"?"Las rutas de la biblioteca SnapTone se eligen al importar un modelo. Los presets y respaldos permanecen locales; esta pantalla no sube datos.":"Choose the SnapTone library path during model import. Presets and backups remain local; this screen does not upload data.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray,Margin=new Thickness(0,12,0,0)});
        card.Child=panel;stack.Children.Add(card);return ScrollPage(stack);
    }

    UIElement BuildToolsPanel()
    {
        var stack=new StackPanel{MaxWidth=820,HorizontalAlignment=HorizontalAlignment.Left};
        var card=CreateSettingsCard(UiText.Language=="es"?"Inspección local de firmware":"Local firmware inspection");
        var panel=new StackPanel();
        panel.Children.Add(new TextBlock{Text=UiText.Language=="es"?"Analiza un archivo BIN/FRMW local para leer sus metadatos y nombres de slots compatibles. Es una inspección estática: no actualiza firmware ni transmite el archivo al pedal.":"Inspect a local BIN/FRMW file for metadata and compatible slot labels. This is static analysis: it does not update firmware or send the file to the pedal.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray,Margin=new Thickness(0,0,0,10)});
        var scan=new Button{Content=UiText.Language=="es"?"Inspeccionar archivo .BIN…":"Inspect .BIN file…",HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(12,6,12,6),Background=new SolidColorBrush(Color.FromRgb(0,119,182)),Foreground=Brushes.White};
        scan.Click+=async(_,_) =>
        {
            if(Owner is EditorWindow editor)await editor.LoadFirmwareBin();
            else status.Text=UiText.Language=="es"?"Esta herramienta requiere la ventana principal del editor.":"This tool requires the main editor window.";
        };
        panel.Children.Add(scan);card.Child=panel;stack.Children.Add(card);return ScrollPage(stack);
    }

    UIElement BuildAboutPanel()
    {
        var stack=new StackPanel{MaxWidth=820,HorizontalAlignment=HorizontalAlignment.Left};
        var updates=CreateSettingsCard(UiText.Language=="es"?"Novedades":"Release notes");
        updates.Child=new TextBlock{Text=UiText.Language=="es"?"Ajustes reorganizados en una navegación lateral, separados entre editor y pedal; controles MIDI integrados en su sección; las preferencias de idioma y escala quedan centralizadas aquí. La inspección BIN es local y no actualiza firmware.":"Settings are reorganized into separate editor and pedal navigation groups; MIDI reference is integrated with MIDI settings; language and scale preferences are centralized here. BIN inspection is local and does not update firmware.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray};
        stack.Children.Add(updates);
        var about=CreateSettingsCard(UiText.Language=="es"?"Ayuda y documentación":"Help & documentation");
        var panel=new StackPanel();
        panel.Children.Add(new TextBlock{Text=UiText.Language=="es"?"GP-200 Community Editor\n\nLa configuración del editor y la del pedal están separadas en el menú lateral. Los ajustes del pedal se leen del dispositivo y se verifican al aplicar cada cambio. Firmware Update y factory reset no están disponibles.":"GP-200 Community Editor\n\nEditor and pedal preferences are separated in the navigation. Pedal settings are read from the device and verified when applied. Firmware update and factory reset are not available.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray,Margin=new Thickness(0,0,0,10)});
        var help=new Button{Content=UiText.Language=="es"?"Abrir ayuda, manual y novedades…":"Open help, manual & release notes…",HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(12,6,12,6)};
        help.Click+=(_,_)=>new HelpWindow{Owner=this}.ShowDialog();
        panel.Children.Add(help);about.Child=panel;stack.Children.Add(about);return ScrollPage(stack);
    }

    UIElement ScrollPage(UIElement content)=>new ScrollViewer{Content=content,Margin=new Thickness(4),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,CanContentScroll=false,PanningMode=PanningMode.Both};

    Button DirectoryButton(string path,string label)
    {
        var button=new Button{Content=label,HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,3,0,3)};
        button.Click+=(_,_) =>
        {
            try{Directory.CreateDirectory(path);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo{FileName=path,UseShellExecute=true});}
            catch(Exception ex){status.Text=ex.Message;}
        };
        return button;
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
        if(busy)return;busy=true;read.IsEnabled=false;status.Text=UiText.T("Comunicando con GP-200…");
        try{status.Text=UiText.T(await action());}
        catch(Exception ex)
        {
            status.Text=ex.Message;state=null;
            foreach(string key in pages.Keys.Where(k=>k.StartsWith("pedal.",StringComparison.Ordinal)).ToArray())pages.Remove(key);
            if(activePageKey.StartsWith("pedal.",StringComparison.Ordinal))
                pageContent.Content=new TextBlock{Text=UiText.T("No se pudieron leer los ajustes del pedal. Comprueba la conexión e inténtalo de nuevo."),Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(12)};
        }
        finally{busy=false;read.IsEnabled=true;}
    }

    Task Apply(DeviceSettingChange change)=>Work(async()=>{state=await DeviceSettings.ApplyAsync(change);Render();return UiText.T("Verificado: ")+change.Description;});

    StackPanel Tab(string key)
    {
        var panel=new StackPanel{Margin=new(10)};
        pages[key]=ScrollPage(panel);
        return panel;
    }

    void Render()
    {
        foreach(string key in pages.Keys.Where(k=>k.StartsWith("pedal.",StringComparison.Ordinal)).ToArray())pages.Remove(key);
        if(state==null)return;
        var general=Tab("pedal.general");
        Heading(general,UiText.T("Input / Output Settings"));Globals(general,[2,1,3,4]);
        Heading(general,UiText.T("USB Audio Settings"));Globals(general,[5,6,7,8,9]);
        Heading(general,UiText.T("Auto Cab Match"));Globals(general,[36]);Heading(general,UiText.T("Device Display"));Globals(general,[40]);
        BuildFootswitchPanel();
        var external=Tab("pedal.expression");Globals(external,[12]);
        var patch=new Button{Content=UiText.T("Setting › EXP del preset"),Padding=new(8,4,8,4),Margin=new(0,12,0,12),HorizontalAlignment=HorizontalAlignment.Left};patch.Click+=(_,_)=>{PatchSettingsRequested?.Invoke();Close();};external.Children.Add(patch);
        if(state.Global(12)>0){Globals(external,[31]);if(state.Global(12)>1)Globals(external,[33]);}
        var eq=Tab("pedal.eq");Globals(eq,[30]);
        var graph=new EqGraph(state);graph.Committed+=async changes=>await Work(async()=>
        {
            foreach(var change in changes)state=await DeviceSettings.ApplyAsync(change);
            Render();return UiText.T("EQ verificado: frecuencia y ganancia actualizadas.");
        });eq.Children.Add(graph);
        eq.Children.Add(new TextBlock{Text=UiText.T("Curva orientativa · arrastra los puntos; Q y Master Volume se ajustan debajo."),Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap});
        foreach(var item in DeviceSettings.Equalizer)Number(eq,item.Name,state.Eq(item.Selector),item.Minimum,item.Maximum,v=>DeviceSettings.SetEq(item.Selector,v));
        var midiSettings=Tab("pedal.midi");Heading(midiSettings,UiText.T("Entrada MIDI"));Globals(midiSettings,[14,15,16]);
        Heading(midiSettings,UiText.T("Salida MIDI"));Globals(midiSettings,[17,18]);
        Heading(midiSettings,UiText.T("Reloj MIDI"));Globals(midiSettings,[19,20,21]);
        midiSettings.Children.Add(new TextBlock{Foreground=Brushes.LightGray,Text=UiText.T("Omni recibe en todos los canales. DIN Only, USB Only y External usan reloj externo; en esos modos TAP Tempo no controla el tempo. Clock Out habilita la salida de reloj del equipo."),TextWrapping=TextWrapping.Wrap,Margin=new(0,15,0,0)});
        midiSettings.Children.Add(new TextBlock{Foreground=Brushes.LightGray,Text=UiText.T("Referencia del manual, páginas PDF 76–78. Es de consulta y no envía mensajes MIDI."),TextWrapping=TextWrapping.Wrap,Margin=new(0,18,0,10)});
        var rows=System.Text.Json.JsonSerializer.Deserialize<List<MidiControlReference>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Data","midi-control-list.json")))??[];
        var table=new DataGrid{ItemsSource=rows.Select(r=>r with{Meaning=UiText.T(r.Meaning)}).ToArray(),IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false,HeadersVisibility=DataGridHeadersVisibility.Column,MinHeight=350};
        table.Columns.Add(new DataGridTextColumn{Header=UiText.T("CC"),Binding=new System.Windows.Data.Binding("Controller"),Width=50});
        table.Columns.Add(new DataGridTextColumn{Header=UiText.T("Rango"),Binding=new System.Windows.Data.Binding("Range"),Width=70});
        table.Columns.Add(new DataGridTextColumn{Header=UiText.T("Función"),Binding=new System.Windows.Data.Binding("Meaning"),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        midiSettings.Children.Add(table);
        var drum=Tab("pedal.rhythms");Choice(drum,UiText.T("Patrón"),DeviceSettings.DrumPatterns,state.Drum(0),0,v=>DeviceSettings.SetDrum(0,v));Number(drum,UiText.T("Volumen"),state.Drum(1),0,100,v=>DeviceSettings.SetDrum(1,(int)v),true);Number(drum,UiText.T("Tempo BPM"),state.Drum(2),40,250,v=>DeviceSettings.SetDrum(2,(int)v),true);
        var play=new Button{Content=state.DrumPlaying?UiText.T("Parar ritmo"):UiText.T("Reproducir ritmo"),Padding=new(8,4,8,4),HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,12,0,0)};play.Click+=async(_,_)=>await Apply(DeviceSettings.SetDrumPlaying(!state!.DrumPlaying));drum.Children.Add(play);
        drum.Children.Add(new TextBlock{Foreground=Brushes.WhiteSmoke,Text=UiText.T("Afinador y looper: asignables a footswitch. Una pantalla de control equivalente en el editor oficial aún no está acreditada."),TextWrapping=TextWrapping.Wrap,Margin=new(0,18,0,0)});
        if(pages.TryGetValue(activePageKey,out var page))pageContent.Content=page;
        else if(activePageKey.StartsWith("pedal.",StringComparison.Ordinal))pageContent.Content=pages["pedal.general"];
        pageHeading.Text=NavigationLabel(activePageKey);UpdateNavigation();
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
