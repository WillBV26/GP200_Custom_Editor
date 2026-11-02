using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
namespace Gp200.Replica;
public sealed partial class EditorWindow
{
    readonly ComboBox librarySource=new(){ItemsSource=new[]{UiText.T("Archivos locales"),UiText.T("GP-200 · 256 posiciones")},SelectedIndex=0};
    readonly ComboBox bankChoice=new(){ItemsSource=new[]{UiText.T("Todos los bancos")}.Concat(Enumerable.Range(1,64).Select(i=>UiText.T($"Banco {i:00}"))).ToArray(),SelectedIndex=0};
    readonly ComboBox collectionFilter=new(){SelectedIndex=0,MinWidth=150,Margin=new Thickness(0,8,0,0)};
    readonly TextBox setlistName=new(){MinWidth=150,Margin=new Thickness(0,6,0,4)};
    readonly Button favoritePresetButton=new(){Cursor=System.Windows.Input.Cursors.Hand};
    readonly DockPanel algorithmHost=new(){LastChildFill=true};
    readonly ContentControl metadataHost=new();
    IReadOnlyList<PresetStyle> presetStyles=[];
    bool loadingLibrary;
    Action? commitPresetName;
    string algorithmFilter="";
    byte[]? copiedPreset;
    ScenePresetBundle? copiedScenes;
    string copiedPresetLabel="";
    LibraryEntry? menuTarget;
    LibraryEntry? pressedDevicePreset;
    async void CopyWorkspacePreset(LibraryEntry? source)
    {
        if(source==null||liveBusy||livePump||loadingLibrary)return;
        copiedScenes=null;
        copiedPreset=(source==liveEntry?desiredLive??source.Document:source.Document).ToBytes();
        copiedPresetLabel=source.ToString();status.Text=UiText.T("Copiado: ")+copiedPresetLabel+UiText.T(". Selecciona el destino y usa Pegar preset.");
        if(CatalogRuntime.Current.IsMx42&&source.DeviceSlot is int slot)
        {
            liveBusy=true;
            try{status.Text="Copying preset and Scenes…";copiedScenes=ScenePresetBundle.Wrap(await new Mx42PresetTransactions("clipboard").ExportAsync(slot,true));status.Text="Copied preset and Scenes: "+copiedPresetLabel;}
            catch(Exception e){copiedPreset=null;status.Text=e.Message;UiDialogs.Show(this,e.Message,"Copy Scenes");}
            finally{liveBusy=false;}
        }
    }
    async Task PasteWorkspacePreset(LibraryEntry? target)
    {
        if(target?.DeviceSlot is not int slot||copiedPreset==null){status.Text=UiText.T("Copia primero un preset y elige una posición de la GP-200.");return;}
        if(live==null){status.Text=UiText.T("Conecta la GP-200 antes de pegar.");return;}
        if(liveBusy||livePump||loadingLibrary)return;
        var document=new PresetDocument((byte[])copiedPreset.Clone());
        if(UiDialogs.Show(this,UiText.T($"Pegar {copiedPresetLabel} en {BankService.Position(slot)}? Se reemplazará el destino y se conservará una copia previa."),UiText.T("Pegar preset"),MessageBoxButton.OKCancel,MessageBoxImage.Question)!=MessageBoxResult.OK)return;
        liveBusy=true;
        try
        {
            var service=new BankService();var saved=copiedScenes!=null?await service.ImportSceneAsync(slot,copiedScenes):await service.ReplaceAsync(slot,document);
            target.Original=saved.ToBytes();target.EditingBaseline=saved.ToBytes();target.Document=saved;target.Undo.Clear();target.Redo.Clear();
            if(liveEntry?.DeviceSlot==slot)
            {
                var actual=await service.SelectAsync(slot,reload:true);live.AcceptSnapshot(actual);liveEntry.Document=actual;
            }
            RefreshLibrary();Render();status.Text=UiText.T("Preset pegado y verificado en ")+BankService.Position(slot)+UiText.T(". Copia previa: ")+service.LastBackupDirectory;
        }
        catch(Exception error){status.Text=error.Message;UiDialogs.Show(this,error.Message,UiText.T("Pegar preset"));}
        finally{liveBusy=false;}
    }
    IEnumerable<LibraryEntry> VisibleEntries()
    {
        var visible=entries.Where(e=>(librarySource.SelectedIndex==1?e.DeviceSlot.HasValue:!e.DeviceSlot.HasValue)&&(bankChoice.SelectedIndex<=0||(e.DeviceSlot??e.Document.Index)/4==bankChoice.SelectedIndex-1));
        if(collectionFilter.SelectedIndex==1&&collections!=null)return visible.Where(collections.IsFavorite);
        if(collectionFilter.SelectedItem is string setlist&&collectionFilter.SelectedIndex>=2&&collections!=null)
        {
            var byKey=visible.ToDictionary(PresetCollections.Key);
            return collections.Entries(setlist).Where(byKey.ContainsKey).Select(key=>byKey[key]);
        }
        return librarySource.SelectedIndex==1?visible.OrderBy(e=>e.DeviceSlot):visible;
    }
    UIElement BuildWorkspaceSidebar()
    {
        var panel=new DockPanel{Background=PanelBrush,LastChildFill=true};
        DarkUiTheme.ApplyToElement(panel);
        var top=new StackPanel{Margin=new Thickness(12)};DockPanel.SetDock(top,Dock.Top);panel.Children.Add(top);
        top.Children.Add(Text(UiText.T("PRESETS"),15,FontWeights.SemiBold));top.Children.Add(librarySource);top.Children.Add(bankChoice);
        librarySource.SelectionChanged+=(_,_)=>
        {
            if(updating)return;
            RefreshLibrary();
            if(librarySource.SelectedIndex==1&&entries.Count(entry=>entry.DeviceSlot.HasValue)<256)
            {
                if(live!=null&&!liveBusy&&!livePump&&!loadingLibrary)_=LoadDeviceLibrary();
                else if(live==null)status.Text=UiText.T("Connect the GP-200 to read the device preset bank.");
            }
        };
        bankChoice.SelectionChanged+=(_,_)=>RefreshLibrary();
        try{collections=PresetCollections.Load();}
        catch(Exception error){status.Text=UiText.T("Local collections could not be loaded: ")+error.Message;}
        collectionFilter.ItemsSource=new[]{UiText.T("All presets"),UiText.T("Favorites")};
        collectionFilter.SelectionChanged+=(_,_)=>{if(!updating)RefreshLibrary();};
        top.Children.Add(collectionFilter);
        setlistName.ToolTip=UiText.T("Song or setlist name");
        top.Children.Add(setlistName);
        search.Margin=new Thickness(0,8,0,8);search.ToolTip=UiText.Language=="es"?"Buscar por nombre (ej. Fender, Lead) o posición (ej. 01-A)":"Search by name (e.g. Fender, Lead) or position (e.g. 01-A)";search.TextChanged+=(_,_)=>RefreshLibrary();top.Children.Add(search);
        var actions=new WrapPanel();
        actions.Children.Add(Button(UiText.T("←"),()=>NavigatePreset(-1)));actions.Children.Add(Button(UiText.T("→"),()=>NavigatePreset(1)));
        actions.Children.Add(favoritePresetButton);favoritePresetButton.Click+=(_,_)=>ToggleFavoritePreset();
        actions.Children.Add(Button(UiText.T("Add to song"),AddPresetToSetlist));
        actions.Children.Add(Button(UiText.T("Remove from setlist"),RemovePresetFromSetlist));

        var saveBtn = Button(UiText.T("Guardar"),()=>_=SaveDevice());
        saveBtn.Background = (Brush)new BrushConverter().ConvertFromString("#059669")!;
        saveBtn.Foreground = Brushes.White;
        saveBtn.FontWeight = FontWeights.Bold;
        actions.Children.Add(saveBtn);

        actions.Children.Add(Button(UiText.Language=="es"?"Guardar como…":"Save As…",()=>_=SaveDeviceAs()));
        actions.Children.Add(Button(UiText.T("Importar…"),()=>_=ImportWorkspace()));
        actions.Children.Add(Button(UiText.T("Exportar…"),Export));
        actions.Children.Add(Button(UiText.T("Export selected ZIP"),ExportSelectedZip));
        actions.Children.Add(Button(UiText.T("Recargar"),()=>_=ReloadWorkspace()));

        var read256Btn = Button(UiText.T("Read 256 presets"),()=>_=ReadDeviceLibrary());
        read256Btn.Background = (Brush)new BrushConverter().ConvertFromString("#0284C7")!;
        read256Btn.Foreground = Brushes.White;
        read256Btn.FontWeight = FontWeights.SemiBold;
        actions.Children.Add(read256Btn);

        actions.Children.Add(Button(UiText.T("Copiar"),()=>CopyWorkspacePreset(library.SelectedItem as LibraryEntry??current)));
        actions.Children.Add(Button(UiText.T("Pegar…"),()=>_=PasteWorkspacePreset(library.SelectedItem as LibraryEntry??current)));
        actions.Children.Add(Button(UiText.Language=="es"?"Respaldo total…":"Full Backup…",()=>_=ExportFullBackup()));
        actions.Children.Add(Button(UiText.T("Full Bank ZIP"),ExportDeviceBackupZip));
        actions.Children.Add(Button(UiText.Language=="es"?"Ajuste masivo…":"Bulk Apply…",OpenBulkApplyDialog));
        top.Children.Add(actions);
        foreach(var action in actions.Children.OfType<Button>()){action.Padding=new Thickness(8,7,8,7);action.Margin=new Thickness(0,0,5,5);action.FontSize=11;}
        metadataHost.Margin=new Thickness(12);DockPanel.SetDock(metadataHost,Dock.Bottom);panel.Children.Add(metadataHost);
        library.Background=PanelBrush;library.Foreground=TextBrush;library.BorderThickness=new Thickness(0);library.Padding=new Thickness(8);library.SelectionMode=SelectionMode.Extended;library.SelectionChanged+=(_,_)=>SelectEntry();
        var presetMenu=DarkContextMenu(this);
        var copy=new MenuItem{Header=UiText.T("Copiar preset")};var paste=new MenuItem{Header=UiText.T("Pegar preset aquí…")};var rename=new MenuItem{Header=UiText.T("Renombrar")};
        var duplicate=new MenuItem{Header=UiText.T("Duplicate as local copy…")};
        var move=new MenuItem{Header=UiText.T("Move to slot…")};var swap=new MenuItem{Header=UiText.T("Swap with slot…")};
        var favorites=new MenuItem{Header=UiText.T("Toggle selected favorites")};var addSetlist=new MenuItem{Header=UiText.T("Add selected to song")};
        var removeSetlist=new MenuItem{Header=UiText.T("Remove selected from setlist")};var exportSelection=new MenuItem{Header=UiText.T("Export selected ZIP")};
        presetMenu.Items.Add(rename);presetMenu.Items.Add(duplicate);presetMenu.Items.Add(move);presetMenu.Items.Add(swap);
        presetMenu.Items.Add(new Separator());presetMenu.Items.Add(copy);presetMenu.Items.Add(paste);
        presetMenu.Items.Add(new Separator());presetMenu.Items.Add(favorites);presetMenu.Items.Add(addSetlist);presetMenu.Items.Add(removeSetlist);presetMenu.Items.Add(exportSelection);library.ContextMenu=presetMenu;
        library.PreviewMouseRightButtonDown+=(_,e)=>
        {
            var clicked=(ItemsControl.ContainerFromElement(library,e.OriginalSource as DependencyObject) as ListBoxItem)?.Content as LibraryEntry;
            if(clicked!=null&&!library.SelectedItems.Contains(clicked)){library.SelectedItems.Clear();library.SelectedItem=clicked;}
            menuTarget=clicked;
            rename.IsEnabled=menuTarget!=null&&!liveBusy&&!loadingLibrary&&(!menuTarget.DeviceSlot.HasValue||live!=null);
            duplicate.IsEnabled=menuTarget!=null&&!liveBusy&&!livePump&&!loadingLibrary;
            move.IsEnabled=menuTarget?.DeviceSlot!=null&&live!=null&&library.SelectedItems.Count<=1&&!liveBusy&&!livePump&&!loadingLibrary;
            swap.IsEnabled=move.IsEnabled;
            copy.IsEnabled=menuTarget!=null&&!liveBusy&&!loadingLibrary;paste.IsEnabled=menuTarget?.DeviceSlot!=null&&copiedPreset!=null&&live!=null&&!liveBusy&&!loadingLibrary;
            favorites.IsEnabled=library.SelectedItems.Count>0&&collections!=null;addSetlist.IsEnabled=library.SelectedItems.Count>0&&collections!=null;
            removeSetlist.IsEnabled=library.SelectedItems.Count>0&&collections!=null&&collectionFilter.SelectedIndex>=2;exportSelection.IsEnabled=library.SelectedItems.Count>0;
        };
        library.ContextMenuOpening+=(_,e)=>
        {
            if(e.CursorLeft<0)menuTarget=library.SelectedItem as LibraryEntry;
            var target=menuTarget;
            if(target==null){e.Handled=true;return;}
            rename.IsEnabled=!liveBusy&&!loadingLibrary&&(!target.DeviceSlot.HasValue||live!=null);
            duplicate.IsEnabled=!liveBusy&&!livePump&&!loadingLibrary;
            move.IsEnabled=target.DeviceSlot!=null&&live!=null&&library.SelectedItems.Count<=1&&!liveBusy&&!livePump&&!loadingLibrary;
            swap.IsEnabled=move.IsEnabled;
            copy.IsEnabled=!liveBusy&&!loadingLibrary;paste.IsEnabled=target.DeviceSlot!=null&&copiedPreset!=null&&live!=null&&!liveBusy&&!loadingLibrary;
            favorites.IsEnabled=library.SelectedItems.Count>0&&collections!=null;addSetlist.IsEnabled=library.SelectedItems.Count>0&&collections!=null;
            removeSetlist.IsEnabled=library.SelectedItems.Count>0&&collections!=null&&collectionFilter.SelectedIndex>=2;exportSelection.IsEnabled=library.SelectedItems.Count>0;
        };
        rename.Click+=async(_,_)=>await RenameWorkspacePreset(menuTarget);
        duplicate.Click+=(_,_)=>DuplicateWorkspacePreset(menuTarget);
        move.Click+=async(_,_)=>await MoveOrSwapPreset(menuTarget,false);
        swap.Click+=async(_,_)=>await MoveOrSwapPreset(menuTarget,true);
        copy.Click+=(_,_)=>CopyWorkspacePreset(menuTarget);paste.Click+=async(_,_)=>await PasteWorkspacePreset(menuTarget);
        favorites.Click+=(_,_)=>ToggleFavoritePreset();addSetlist.Click+=(_,_)=>AddPresetToSetlist();
        removeSetlist.Click+=(_,_)=>RemovePresetFromSetlist();exportSelection.Click+=(_,_)=>ExportSelectedZip();
        library.PreviewMouseLeftButtonDown+=(_,e)=>
        {
            pressedDevicePreset=null;
            if(ItemsControl.ContainerFromElement(library,e.OriginalSource as DependencyObject) is ListBoxItem item&&item.Content is LibraryEntry selected&&selected.DeviceSlot.HasValue)
            {
                if((System.Windows.Input.Keyboard.Modifiers&(System.Windows.Input.ModifierKeys.Control|System.Windows.Input.ModifierKeys.Shift))!=0)return;
                pressedDevicePreset=selected;library.CaptureMouse();e.Handled=true;
            }
        };
        library.PreviewMouseLeftButtonUp+=(_,e)=>
        {
            var selected=pressedDevicePreset;pressedDevicePreset=null;
            if(selected!=null){library.ReleaseMouseCapture();e.Handled=true;_=SelectDevicePreset(selected);}
        };
        library.LostMouseCapture+=(_,_)=>pressedDevicePreset=null;
        library.PreviewKeyDown+=(_,e)=>
        {
            if(librarySource.SelectedIndex!=1||e.Key is not (System.Windows.Input.Key.Up or System.Windows.Input.Key.Down or System.Windows.Input.Key.Enter))return;
            e.Handled=true;if(liveBusy||livePump||loadingLibrary)return;
            var rows=library.Items.Cast<LibraryEntry>().ToArray();int at=Array.IndexOf(rows,current);
            if(e.Key!=System.Windows.Input.Key.Enter)at+=e.Key==System.Windows.Input.Key.Down?1:-1;
            if(at>=0&&at<rows.Length)_=SelectDevicePreset(rows[at]);
        };
        RefreshCollectionChoices();
        panel.Children.Add(library);return panel;
    }

    void RefreshCollectionChoices()
    {
        string? selected=collectionFilter.SelectedItem as string;
        var choices=new[]{UiText.T("All presets"),UiText.T("Favorites")}.Concat(collections?.SetlistNames??[]).ToArray();
        collectionFilter.ItemsSource=choices;
        collectionFilter.SelectedItem=selected!=null&&choices.Contains(selected,StringComparer.OrdinalIgnoreCase)?selected:choices[0];
    }

    void UpdateCollectionActions()
    {
        var entry=SelectedCollectionEntry();
        favoritePresetButton.Content=UiText.T(entry!=null&&collections?.IsFavorite(entry)==true?"Remove from Favorites":"Add to Favorites");
        favoritePresetButton.IsEnabled=entry!=null&&collections!=null;
    }

    LibraryEntry? SelectedCollectionEntry()=>library.SelectedItem as LibraryEntry??current;

    LibraryEntry[] SelectedCollectionEntries()
    {
        var selected=library.SelectedItems.OfType<LibraryEntry>().ToArray();
        if(selected.Length!=0)return selected;
        var fallback=menuTarget??current;
        return fallback==null?[]:[fallback];
    }

    void ToggleFavoritePreset()
    {
        var selected=SelectedCollectionEntries();
        if(selected.Length==0||collections==null){status.Text=UiText.T("Local collections are unavailable.");return;}
        try
        {
            bool allFavorite=selected.All(collections.IsFavorite);
            collections.ToggleFavorites(selected);RefreshLibrary();
            status.Text=UiText.T(allFavorite?"Preset removed from Favorites.":"Preset added to Favorites.");
        }
        catch(Exception error){status.Text=error.Message;UiDialogs.Show(this,error.Message,UiText.T("Favorites"));}
    }

    void AddPresetToSetlist()
    {
        var selected=SelectedCollectionEntries();string name=setlistName.Text.Trim();
        if(selected.Length==0||collections==null){status.Text=UiText.T("Local collections are unavailable.");return;}
        try
        {
            collections.AddManyToSetlist(name,selected);RefreshCollectionChoices();
            string canonicalName=collectionFilter.Items.Cast<string>().FirstOrDefault(choice=>choice.Equals(name,StringComparison.OrdinalIgnoreCase))??name;
            collectionFilter.SelectedItem=canonicalName;RefreshLibrary();
            status.Text=UiText.T("Presets added to setlist: ")+selected.Length+" · "+canonicalName;
        }
        catch(Exception error){status.Text=error.Message;UiDialogs.Show(this,error.Message,UiText.T("Song setlist"));}
    }

    void RemovePresetFromSetlist()
    {
        if(collections==null||collectionFilter.SelectedIndex<2||collectionFilter.SelectedItem is not string name)
        {status.Text=UiText.T("Select a song list to remove the preset from.");return;}
        var selected=SelectedCollectionEntries();if(selected.Length==0)return;
        try
        {
            int removed=collections.RemoveManyFromSetlist(name,selected);
            if(removed==0){status.Text=UiText.T("The preset is not in this setlist.");return;}
            RefreshLibrary();status.Text=UiText.T("Presets removed from setlist: ")+removed+" · "+name;
        }
        catch(Exception error){status.Text=error.Message;UiDialogs.Show(this,error.Message,UiText.T("Song setlist"));}
    }

    void ExportSelectedZip()
    {
        var selected=SelectedCollectionEntries();
        if(selected.Length==0){status.Text=UiText.T("Select presets to export.");return;}
        var dialog=new SaveFileDialog
        {
            Filter=UiText.Filter("ZIP archives (*.zip)|*.zip"),
            FileName="GP200-presets.zip",
            AddExtension=true,
            DefaultExt=".zip",
            OverwritePrompt=true
        };
        if(dialog.ShowDialog(this)!=true)return;
        string target=Path.ChangeExtension(dialog.FileName,".zip");
        try
        {
            var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var presets=selected.Select(entry=>
            {
                var document=entry==liveEntry?desiredLive??entry.Document:entry.Document;
                string safeName=string.Concat(document.Name.Select(character=>Path.GetInvalidFileNameChars().Contains(character)?'_':character)).Trim();
                if(string.IsNullOrWhiteSpace(safeName))safeName="Preset";
                string baseName=$"{BankService.Position(entry.DeviceSlot??document.Index)}_{safeName}";
                string name=baseName+".prst";
                for(int suffix=2;!names.Add(name);suffix++)name=$"{baseName}_{suffix}.prst";
                return(name,document.ToBytes());
            }).ToArray();
            PresetArchive.Write(target,presets,overwrite:true);
            status.Text=UiText.T("Presets exported to ZIP: ")+presets.Length+" · "+target;
        }
        catch(Exception error){status.Text=error.Message;UiDialogs.Show(this,error.Message,UiText.T("Export ZIP"));}
    }

    async void ExportDeviceBackupZip()
    {
        if(liveBusy||livePump||loadingLibrary)return;
        if(live==null){status.Text=UiText.T("Connect the GP-200 to create a full device backup.");return;}
        var dialog=new SaveFileDialog
        {
            Filter=UiText.Filter("ZIP archives (*.zip)|*.zip"),
            FileName=$"GP200_Backup_{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip",
            AddExtension=true,
            DefaultExt=".zip",
            OverwritePrompt=true
        };
        if(dialog.ShowDialog(this)!=true)return;
        liveBusy=true;
        try
        {
            var progress=new Progress<int>(read=>status.Text=UiText.Language=="es"
                ?$"Leyendo presets de GP-200 · {read}/256"
                :$"Reading GP-200 presets · {read}/256");
            var slots=await new BankService().ReadLibraryAsync(progress);
            if(slots.Count!=256||!slots.Select(entry=>entry.Slot).SequenceEqual(Enumerable.Range(0,256)))
                throw new InvalidDataException(UiText.T("The complete set of 256 device presets could not be read."));
            var presets=slots.Select(entry=>
            {
                var stored=entry.Document;
                string safeName=string.Concat(stored.Name.Select(character=>Path.GetInvalidFileNameChars().Contains(character)?'_':character)).Trim();
                if(string.IsNullOrWhiteSpace(safeName))safeName="Preset";
                return($"{BankService.Position(entry.Slot)}_{safeName}.prst",stored.ToBytes());
            }).ToArray();
            string target=Path.ChangeExtension(dialog.FileName,".zip");
            PresetArchive.Write(target,presets,overwrite:true);
            status.Text=UiText.T("Full 256-preset backup exported: ")+target;
        }
        catch(Exception error){status.Text=error.Message;UiDialogs.Show(this,error.Message,UiText.T("Full Backup"));}
        finally{liveBusy=false;}
    }

    void DuplicateWorkspacePreset(LibraryEntry? source)
    {
        if(source==null||liveBusy||livePump||loadingLibrary)return;
        var document=source==liveEntry?desiredLive??source.Document:source.Document;
        string safeName=string.Concat(document.Name.Select(character=>Path.GetInvalidFileNameChars().Contains(character)?'_':character)).Trim();
        if(string.IsNullOrWhiteSpace(safeName))safeName="Preset";
        var dialog=new SaveFileDialog
        {
            Filter=UiText.Filter("GP-200 preset (*.prst)|*.prst"),
            FileName=$"{BankService.Position(source.DeviceSlot??document.Index)}_{safeName}_copy.prst",
            AddExtension=true,
            DefaultExt=".prst",
            OverwritePrompt=false
        };
        if(dialog.ShowDialog(this)!=true)return;
        string target=Path.ChangeExtension(dialog.FileName,".prst");
        try
        {
            if(File.Exists(target)){status.Text=UiText.T("Choose a new filename to preserve existing files.");return;}
            using(var stream=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                var bytes=document.ToBytes();
                stream.Write(bytes);
            }
            Load(target);
            var duplicate=entries.Single(entry=>StringComparer.OrdinalIgnoreCase.Equals(entry.Path,Path.GetFullPath(target)));
            librarySource.SelectedIndex=0;current=duplicate;RefreshLibrary();library.SelectedItem=duplicate;Render();
            status.Text=UiText.T("Local copy created: ")+target;
        }
        catch(Exception error){status.Text=error.Message;UiDialogs.Show(this,error.Message,UiText.T("Duplicate preset"));}
    }

    async Task MoveOrSwapPreset(LibraryEntry? source,bool swap)
    {
        if(source?.DeviceSlot is not int sourceSlot||live==null||liveBusy||livePump||loadingLibrary)
        {
            status.Text=UiText.T("Connect the GP-200 and select a device memory slot first.");
            return;
        }
        if(liveEntry?.Dirty==true)
        {
            status.Text=UiText.T("Save or discard the active preset's pending edits before moving or swapping slots.");
            return;
        }

        var names=entries.Where(entry=>entry.DeviceSlot.HasValue).GroupBy(entry=>entry.DeviceSlot!.Value)
            .ToDictionary(group=>group.Key,group=>group.First().Document.Name);
        var dialog=new SavePresetAsWindow(sourceSlot,names)
        {
            Owner=this,
            Title=UiText.T(swap?"Swap preset slots":"Move preset")
        };
        if(dialog.ShowDialog()!=true)return;
        int destination=dialog.SelectedSlot;
        if(destination==sourceSlot)return;

        string prompt=swap
            ?UiText.Language=="es"
                ?$"¿Intercambiar {BankService.Position(sourceSlot)} con {BankService.Position(destination)}? Se reemplazarán ambos slots y se conservará una copia automática."
                :$"Swap {BankService.Position(sourceSlot)} with {BankService.Position(destination)}? Both device slots will be replaced; an automatic backup will be kept."
            :UiText.Language=="es"
                ?$"¿Mover {BankService.Position(sourceSlot)} a {BankService.Position(destination)}? Se reemplazará el destino, se vaciará el origen y se conservará una copia automática."
                :$"Move {BankService.Position(sourceSlot)} to {BankService.Position(destination)}? The destination will be replaced and the source cleared; an automatic backup will be kept.";
        if(UiDialogs.Show(this,prompt,dialog.Title,MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        if(liveBusy||livePump||loadingLibrary||live==null)return;

        liveBusy=true;
        var service=new BankService();
        try
        {
            int previousActive=liveEntry?.DeviceSlot??-1;
            if(swap)await service.SwapAsync(sourceSlot,destination);
            else await service.CopyAsync(sourceSlot,destination,move:true);

            var sourceStored=await service.ReadAsync(sourceSlot);
            var destinationStored=await service.ReadAsync(destination);
            UpdateStoredSlot(sourceSlot,sourceStored);
            UpdateStoredSlot(destination,destinationStored);

            if(previousActive==sourceSlot||previousActive==destination)
            {
                int reloadSlot=!swap&&previousActive==sourceSlot?destination:previousActive;
                var actual=await service.SelectAsync(reloadSlot,reload:true);
                var session=live!.ForSelectedPreset(actual);
                await session.RefreshExpressionStateAsync();
                live=session;
                var activeEntry=entries.Single(entry=>entry.DeviceSlot==reloadSlot);
                activeEntry.Document=session.Baseline;
                activeEntry.EditingBaseline=session.Baseline.ToBytes();
                activeEntry.Undo.Clear();activeEntry.Redo.Clear();
                liveEntry=current=activeEntry;
            }

            RefreshLibrary();Render();
            status.Text=UiText.T(swap?"Preset slots swapped. Backup: ":"Preset moved. Backup: ")+service.LastBackupDirectory;
        }
        catch(Exception error)
        {
            status.Text=error.Message;
            UiDialogs.Show(this,error.Message,UiText.T(swap?"Swap preset slots":"Move preset"));
        }
        finally{liveBusy=false;}
    }

    void UpdateStoredSlot(int slot,PresetDocument document)
    {
        var entry=entries.FirstOrDefault(item=>item.DeviceSlot==slot);
        if(entry==null)
        {
            entry=new LibraryEntry{Path=$"GP-200 · {BankService.Position(slot)}",Original=document.ToBytes(),Document=document,DeviceSnapshot=true,DeviceSlot=slot};
            entries.Add(entry);
        }
        entry.Original=document.ToBytes();
        entry.Document=document;
        entry.EditingBaseline=document.ToBytes();
        entry.Undo.Clear();entry.Redo.Clear();entry.DiscardSceneChanges();
    }
    async Task ReloadWorkspace()
    {
        if(liveBusy||livePump||loadingLibrary)return;
        if(live==null){await ToggleLive();return;}
        try{await LoadDeviceLibrary(true);}catch(Exception e){status.Text=e.Message;}
    }
    async Task ReadDeviceLibrary()
    {
        if(live==null){status.Text=UiText.T("Connect the GP-200 to read the device preset bank.");return;}
        if(liveBusy||livePump||loadingLibrary)return;
        try{await LoadDeviceLibrary(true);}
        catch(Exception error){status.Text=error.Message;UiDialogs.Show(this,error.Message,UiText.T("Read 256 presets"));}
    }
    async Task LoadDeviceLibrary(bool force=false)
    {
        if(loadingLibrary||!force&&entries.Count(e=>e.DeviceSlot.HasValue)==256)return;
        loadingLibrary=true;var service=new BankService();
        try
        {
            var state=await DeviceSession.Current.Read(ReadQuery.State);presetStyles=PresetStyles.Parse(state.Records);
            for(int slot=0;slot<256;slot++)
            {
                var doc=await service.ReadAsync(slot);var item=entries.FirstOrDefault(e=>e.DeviceSlot==slot);
                if(item==null)entries.Add(new LibraryEntry{Path=$"GP-200 · {BankService.Position(slot)}",Original=doc.ToBytes(),Document=doc,DeviceSnapshot=true,DeviceSlot=slot});
                else {item.Original=doc.ToBytes();if(item!=liveEntry)item.Document=doc;}
                if(slot%16==15){RefreshLibrary();status.Text=UiText.T($"Cargando biblioteca GP-200 · {slot+1}/256");}
            }
            librarySource.SelectedIndex=1;RefreshLibrary();Render();status.Text=UiText.T("Biblioteca GP-200 cargada · 64 bancos / 256 presets.");
        }finally{loadingLibrary=false;}
    }
    async Task SelectDevicePreset(LibraryEntry selected)
    {
        if(liveBusy||livePump||loadingLibrary){RefreshLibrary();return;}
        if(live==null){status.Text=UiText.T("Conecta la GP-200 para seleccionar esta posición.");RefreshLibrary();return;}
        int requestedSlot=selected.DeviceSlot!.Value;
        if(liveEntry?.DeviceSlot==requestedSlot){current=liveEntry;RefreshLibrary();Render();return;}
        var previous=liveEntry;liveBusy=true;
        try
        {
            var service=new BankService();
            if(previous!=null&&previous.Dirty)
            {
                var answer=UiDialogs.Show(this,UiText.T($"Guardar los cambios de {BankService.Position(previous.Document.Index)} antes de cambiar de preset?\nSí: guardar · No: descartar · Cancelar: permanecer."),UiText.T("Cambios sin guardar"),MessageBoxButton.YesNoCancel,MessageBoxImage.Question);
                if(answer==MessageBoxResult.Cancel){current=previous;RefreshLibrary();return;}
                if(answer==MessageBoxResult.Yes){var saved=await service.SaveActiveAsync(previous.Document.Index);previous.ConfirmSceneStore();previous.Original=saved.ToBytes();}
            }
            var actual=await service.SelectAsync(requestedSlot);
            if(previous!=null&&previous!=selected){previous.Document=new PresetDocument(previous.Original);previous.EditingBaseline=null;previous.DiscardSceneChanges();}
            var savedDestination=await service.ReadAsync(selected.DeviceSlot.Value);selected.Original=savedDestination.ToBytes();
            var session=live!.ForSelectedPreset(actual);
            await session.RefreshExpressionStateAsync();
            if(session.Baseline.Index!=requestedSlot)throw new IOException(UiText.T($"Se solicitó {BankService.Position(requestedSlot)}, pero el equipo devolvió {BankService.Position(session.Baseline.Index)}. No se aplicará la respuesta a otra fila."));
            live=session;selected.Document=live.Baseline;selected.EditingBaseline=live.Baseline.ToBytes();selected.Undo.Clear();selected.Redo.Clear();current=liveEntry=selected;RefreshLibrary();Render();status.Text=UiText.T("Preset activo · cambios directos, guardar es una acción separada.");
        }
        catch(Exception e){status.Text=e.Message;RefreshLibrary();}
        finally{liveBusy=false;}
    }
    void NavigatePreset(int delta)
    {
        if(current==null)return;
        if(current.DeviceSlot.HasValue){int slot=BankService.AdjacentSlot(current.DeviceSlot.Value,delta);var target=entries.FirstOrDefault(e=>e.DeviceSlot==slot);if(target!=null)_=SelectDevicePreset(target);}
        else{var local=VisibleEntries().ToList();int index=local.IndexOf(current)+delta;if(index>=0&&index<local.Count)library.SelectedItem=local[index];}
    }
    async Task ImportWorkspace()
    {
        if(liveBusy||livePump||loadingLibrary)return;
        if(current?.DeviceSlot is not int destination){Open();return;}
        var dialog=new OpenFileDialog{Filter=UiText.Filter(CatalogRuntime.Current.IsMx42?"GP-200 presets and Scenes|*.prst;*.gpscene;*.zip":"GP-200 presets and ZIP archives|*.prst;*.zip"),Multiselect=true};if(dialog.ShowDialog(this)!=true)return;
        try
        {
            var imported=new List<(int? Slot,PresetDocument Document,ScenePresetBundle? Scenes)>();
            foreach(string path in dialog.FileNames)
            {
                if(Path.GetExtension(path).Equals(".zip",StringComparison.OrdinalIgnoreCase))
                    imported.AddRange(PresetArchive.Read(path).Select(item=>(PresetArchive.SlotFromName(item.Name),item.Document,(ScenePresetBundle?)null)));
                else
                {
                    var scenes=Path.GetExtension(path).Equals(".gpscene",StringComparison.OrdinalIgnoreCase)?ScenePresetBundle.Load(path):null;
                    imported.Add((null,new PresetDocument(scenes?.Preset??File.ReadAllBytes(path)),scenes));
                }
            }
            var docs=imported.Select(item=>item.Document).ToArray();
            if(docs.Length==0)throw new InvalidDataException(UiText.T("ZIP archive contains no GP-200 presets."));
            bool restorePositions=imported.Count>0&&imported.All(item=>item.Slot.HasValue);
            int[] targets=restorePositions?imported.Select(item=>item.Slot!.Value).ToArray():Enumerable.Range(destination,docs.Length).ToArray();
            if(!restorePositions&&destination+docs.Length>256)throw new InvalidOperationException(UiText.T("Los presets no caben desde el destino seleccionado."));
            if(targets.Distinct().Count()!=targets.Length)throw new InvalidOperationException(UiText.T("The selected archive contains duplicate target slots."));
            int[] sortedTargets=targets.Order().ToArray();
            bool contiguous=sortedTargets.SequenceEqual(Enumerable.Range(sortedTargets[0],sortedTargets.Length));
            string targetDescription=contiguous
                ?$"{BankService.Position(sortedTargets[0])}–{BankService.Position(sortedTargets[^1])}"
                :string.Join(", ",targets.Select(BankService.Position));
            string importPrompt=UiText.Language=="es"
                ?$"¿Importar {docs.Length} preset(s) en {targetDescription}? Se reemplazarán esas posiciones. Se conservará copia previa."
                :$"Import {docs.Length} preset(s) to {targetDescription}? Those slots will be replaced; an automatic backup will be kept.";
            if(UiDialogs.Show(this,importPrompt,UiText.T("Importar presets"),MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
            liveBusy=true;var service=new BankService();
            if(imported.Any(item=>item.Scenes!=null))
            {for(int i=0;i<docs.Length;i++)if(imported[i].Scenes is ScenePresetBundle bundle)await service.ImportSceneAsync(targets[i],bundle);else await service.ReplaceAsync(targets[i],docs[i]);}
            else if(targets.SequenceEqual(Enumerable.Range(targets[0],targets.Length)))await service.ReplaceManyAsync(targets[0],docs);
            else for(int i=0;i<docs.Length;i++)await service.ReplaceAsync(targets[i],docs[i]);
            foreach(int targetSlot in targets)UpdateStoredSlot(targetSlot,await service.ReadAsync(targetSlot));
            if(live!=null&&liveEntry?.DeviceSlot is int activeSlot&&targets.Contains(activeSlot))
            {
                var actual=await service.SelectAsync(activeSlot,reload:true);live.AcceptSnapshot(actual);liveEntry.Document=actual;
            }
            RefreshLibrary();Render();status.Text=UiText.T("Importación leída y verificada. Copias previas: ")+service.LastBackupDirectory;
        }catch(Exception e){status.Text=e.Message;UiDialogs.Show(this,e.Message,UiText.T("Importar presets"));}
        finally{liveBusy=false;}
    }
    void RenderWorkspacePanels()
    {
        if(current==null)return;var doc=current.Document;
        UpdateWorkspaceValues(doc);
        patchOverview.Children.Clear();patchOverview.Children.Add(Text(live?.ExpressionState is { } state?$"EXP 1: {state.InternalLabel}   EXP 2: {state.ExternalLabel}":"EXP 1 / EXP 2",11,FontWeights.Normal,Muted));
        var quickTitle=Text("Quick Access",12,FontWeights.SemiBold);quickTitle.Margin=new Thickness(0,16,0,10);patchOverview.Children.Add(quickTitle);
        foreach(var assignment in doc.Quick)
        {
            string target=assignment.Module==12?UiText.T("Patch Tempo"):assignment.Module==11?UiText.T("Patch Volume"):assignment.Module==255?"OFF":assignment.Module<11?EffectLabel(doc,assignment.Module)+" · "+(catalog.Find(assignment.Module,doc.GetEffect(assignment.Module).Code)?.Parameters.FirstOrDefault(p=>p.Slot==assignment.Parameter)?.Name??assignment.Parameter.ToString()):"OFF";
            var line=Text($"{assignment.Slot+1}  {target}",11);line.Margin=new Thickness(0,0,0,8);patchOverview.Children.Add(line);
        }
        ScrollViewer? ListScroll(DependencyObject node)
        {
            if(node is ScrollViewer viewer)return viewer;
            for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);i++)
                if(ListScroll(System.Windows.Media.VisualTreeHelper.GetChild(node,i)) is { } found)return found;
            return null;
        }
        var previousList=algorithmHost.Children.OfType<ListBox>().FirstOrDefault(l=>Equals(l.Tag,$"algorithm-list-{selectedModule}"));
        double? previousOffset=previousList==null?null:ListScroll(previousList)?.VerticalOffset;
        algorithmHost.Children.Clear();AddMx42FamilySelector(doc);
        var filter=new TextBox{Text=algorithmFilter,Margin=new Thickness(0,10,0,8),ToolTip=UiText.T("Buscar algoritmo")};algorithmHost.Children.Add(filter);
        foreach(UIElement header in algorithmHost.Children)DockPanel.SetDock(header,Dock.Top);
        var list=new ListBox{Tag=$"algorithm-list-{selectedModule}",DisplayMemberPath="Name",Background=PanelBrush,Foreground=TextBrush,BorderThickness=new Thickness(0),MaxHeight=490,VerticalAlignment=VerticalAlignment.Top,FontSize=12};
        void Fill(){list.ItemsSource=InspectorAlgorithms(doc).Where(a=>a.Name.Contains(filter.Text,StringComparison.OrdinalIgnoreCase)).ToList();list.SelectedItem=catalog.Find(selectedModule,doc.GetEffect(selectedModule).Code);}
        int inspectorModule=selectedModule;Fill();filter.TextChanged+=(_,_)=>{algorithmFilter=filter.Text;Fill();};list.SelectionChanged+=(_,_)=>{if(ReferenceEquals(list.Parent,algorithmHost)&&selectedModule==inspectorModule&&list.SelectedItem is AlgorithmInfo choice&&current!=null){var editable=current==liveEntry&&desiredLive!=null?desiredLive:current.Document;if(choice.Code!=editable.GetEffect(inspectorModule).Code)Guard(()=>Edit(d=>d.SetAlgorithm(inspectorModule,choice)));}};algorithmHost.Children.Add(list);
        bool sameChoices=previousList!=null&&previousList.Items.Cast<AlgorithmInfo>().Select(a=>a.Code).SequenceEqual(list.Items.Cast<AlgorithmInfo>().Select(a=>a.Code));
        list.Loaded+=(_,_)=>list.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>
        {
            if(!list.IsLoaded||!ReferenceEquals(list.Parent,algorithmHost))return;
            if(sameChoices&&previousOffset.HasValue&&ListScroll(list) is { } scroll){scroll.ScrollToVerticalOffset(previousOffset.Value);list.UpdateLayout();}
            if(list.SelectedItem!=null)list.ScrollIntoView(list.SelectedItem);
        }));
    }
    void OpenPatchSettings()
    {
        if(current==null)return;
        var entry=current;
        var session=live;
        var window=new PatchSettingsWindow(()=>desiredLive??entry.Document,action=>{if(current==entry)Edit(action);},catalog,()=>liveBusy||livePump,
            ()=>live==session?session?.ExpressionState:null,
            async()=>live==session&&session!=null?await session.RefreshExpressionStateAsync():null,
            ()=>status.Text){Owner=this};window.ShowDialog();Render();
    }
    void OpenDesktopTuner()
    {
            if(tunerWindow is { IsVisible: true }){tunerWindow.Activate();return;}
            tunerWindow=new TunerWindow{Owner=this};
            tunerWindow.Closed+=(_,_)=>tunerWindow=null;
            tunerWindow.Show();
    }
    void OpenLoudnessMeter(IReadOnlyList<PresetDocument>? initialPresets = null, Window? owner = null)
    {
        if(loudnessMeterWindow is { IsVisible:true })
        {
            if(initialPresets is { Count: > 0 })
                loudnessMeterWindow.AddPresetsToQueue(initialPresets);
            loudnessMeterWindow.Activate();
            return;
        }
        loudnessMeterWindow=new LoudnessMeterWindow(
            ()=>current?.Document,
            newVol=>Edit(d=>d.SetGeneral(d.Name,newVol,d.Tempo)),
            initialPresets){Owner=owner ?? this};
        loudnessMeterWindow.Closed+=(_,_)=>loudnessMeterWindow=null;
        loudnessMeterWindow.Show();
    }
    async Task OpenPatchAfterSettings(){if(live==null&&ReadOnlyMidi.IsDeviceAvailable())await ToggleLive(true);if(current!=null)OpenPatchSettings();}
    async Task ExportFullBackup()
    {
        if(loadingLibrary||liveBusy||livePump)return;
        var toExport=entries.Where(e=>librarySource.SelectedIndex==1?e.DeviceSlot.HasValue:!e.DeviceSlot.HasValue).ToList();
        if(toExport.Count==0)
        {
            status.Text=UiText.Language=="es"?"No hay presets disponibles para respaldar.":"No presets available to backup.";
            return;
        }

        var folderDialog=new Microsoft.Win32.OpenFolderDialog
        {
            Title=UiText.Language=="es"?"Selecciona carpeta de destino para el respaldo completo":"Select destination folder for full backup"
        };
        if(folderDialog.ShowDialog(this)!=true)return;

        string targetDir=folderDialog.FolderName;
        string timestamp=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        string backupFolder=Path.Combine(targetDir,$"GP200_Backup_{timestamp}");
        Directory.CreateDirectory(backupFolder);

        int count=0;
        foreach(var entry in toExport)
        {
            string prefix=entry.DeviceSlot.HasValue?BankService.Position(entry.DeviceSlot.Value):$"{count+1:00}";
            string safeName=string.Concat(entry.Document.Name.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c)).Trim();
            if(string.IsNullOrEmpty(safeName))safeName="Preset";
            string fileName=$"{prefix}_{safeName}.prst";
            string filePath=Path.Combine(backupFolder,fileName);
            File.WriteAllBytes(filePath,entry.Document.ToBytes());
            count++;
        }

        status.Text=UiText.Language=="es"
            ?$"Respaldo completado: {count} presets exportados en {backupFolder}"
            :$"Backup completed: {count} presets exported to {backupFolder}";
    }

    void OpenBulkApplyDialog()
    {
        if(current==null)return;
        var window=new Window
        {
            Title=UiText.Language=="es"?"Ajuste Masivo (Bulk Apply)":"Bulk Apply Settings",
            Width=440,
            Height=310,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Owner=this,
            ResizeMode=ResizeMode.NoResize
        };
        StyleDialog(window);

        var stack=new StackPanel{Margin=new Thickness(18)};
        stack.Children.Add(Text(UiText.Language=="es"?"Ajuste Masivo de Presets":"Bulk Preset Adjustment",16,FontWeights.Bold));
        var subtitle=Text(UiText.Language=="es"?"Aplica volumen uniforme a todos los presets del banco visible o biblioteca:":"Apply uniform volume across presets in the visible bank or library:",11,brush:Muted);
        subtitle.Margin=new Thickness(0,4,0,14);
        stack.Children.Add(subtitle);

        var volRow=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,12),VerticalAlignment=VerticalAlignment.Center};
        volRow.Children.Add(Text(UiText.Language=="es"?"Volumen (0–100):":"Volume (0–100):",12));
        var volSlider=new Slider{Minimum=0,Maximum=100,Value=current.Document.Volume,Width=160,Margin=new Thickness(12,0,12,0),TickFrequency=1,IsSnapToTickEnabled=true,Cursor=System.Windows.Input.Cursors.Hand};
        ToolTipService.SetShowOnDisabled(volSlider,true);
        var volText=Text(((int)volSlider.Value).ToString(),12,FontWeights.Bold,Accent);
        volSlider.ValueChanged+=(_,e)=>volText.Text=((int)e.NewValue).ToString();
        volRow.Children.Add(volSlider);
        volRow.Children.Add(volText);
        stack.Children.Add(volRow);

        var scopeBank=new RadioButton{Content=UiText.Language=="es"?"Aplicar solo al banco visible actual":"Apply to current visible bank only",IsChecked=true,Foreground=TextBrush,Margin=new Thickness(0,6,0,4)};
        var scopeAll=new RadioButton{Content=UiText.Language=="es"?"Aplicar a todos los presets cargados":"Apply to all loaded presets",Foreground=TextBrush,Margin=new Thickness(0,0,0,18)};
        stack.Children.Add(scopeBank);
        stack.Children.Add(scopeAll);

        var btnRow=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var cancelBtn=new Button{Content=UiText.Language=="es"?"Cancelar":"Cancel",Margin=new Thickness(0,0,8,0),Cursor=System.Windows.Input.Cursors.Hand};
        cancelBtn.Click+=(_,_)=>window.Close();
        var applyBtn=new Button{Content=UiText.Language=="es"?"Aplicar volumen":"Apply volume",Background=Brush("#168F8B"),Foreground=Brushes.White,Cursor=System.Windows.Input.Cursors.Hand};
        applyBtn.Click+=(_,_)=>
        {
            int targetVol=(int)volSlider.Value;
            var targets=scopeBank.IsChecked==true?VisibleEntries().ToList():entries.ToList();
            int modified=0;
            foreach(var entry in targets)
            {
                var doc=entry.Document;
                if(doc.Volume!=targetVol)
                {
                    entry.Undo.Push(doc.ToBytes());
                    entry.Redo.Clear();
                    doc.SetGeneral(doc.Name,targetVol,doc.Tempo);
                    modified++;
                }
            }
            RefreshLibrary();
            Render();
            window.Close();
            status.Text=UiText.Language=="es"?$"Volumen {targetVol} aplicado a {modified} presets.":"Volume {targetVol} applied to {modified} presets.";
        };
        btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(applyBtn);
        stack.Children.Add(btnRow);

        window.Content=stack;
        window.ShowDialog();
    }
}
