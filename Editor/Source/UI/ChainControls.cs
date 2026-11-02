using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Gp200.Replica;

public sealed partial class EditorWindow
{
    readonly CheckBox enableChain=new(){Content="Enable CHAIN",Margin=new Thickness(12,0,2,0),VerticalAlignment=VerticalAlignment.Center,Cursor=Cursors.Hand};
    readonly ContentControl chainOptions=new();
    readonly Dictionary<LibraryEntry,int> nativeChainModes=[];
    readonly Dictionary<LibraryEntry,int> chainSplits=[];
    static string ChainText(string en,string es)=>UiText.Language=="es"?es:en;

    void SetupChainControls()
    {
        enableChain.Style=(Style)FindResource("ChainToggle");
        enableChain.FontSize=12;
        enableChain.Cursor=Cursors.Hand;
        ToolTipService.SetShowOnDisabled(enableChain,true);
        enableChain.ToolTip=ChainText("Enable independent A/B dual routing. Save or Sync writes changes to hardware.","Activa las ramas A/B independientes. Guardar o Sincronizar escribe los cambios en la GP-200.");
        enableChain.Click+=(_,_)=>Guard(()=>
        {
            if(current==null)return;
            var p=current.Document;
            bool next=!ChainRouting.Enabled(p.FxLoopMode);
            if(next)
            {
                var grps=ChainRouting.Groups(p);
                bool branchesEmpty=grps[1].Length==0&&grps[2].Length==0;
                int split=chainSplits.GetValueOrDefault(current,branchesEmpty?6:ChainRouting.Boundary(p));
                if(branchesEmpty)
                {
                    split=6;
                    chainSplits[current]=6;
                    Edit(d=>
                    {
                        var c=Mx42ChainConfig.GetOrDefault(d);
                        c=c with{Start=4,Split=6,End=8};
                        d.SetCustomRouting(0x80|6,4,6,8);
                        c.Write(d);
                    });
                }
                else
                {
                    chainSplits[current]=split;
                    Edit(d=>ChainRouting.SetEnabled(d,true,split,nativeChainModes.GetValueOrDefault(current,0)));
                }
            }
            else
            {
                int split=chainSplits.GetValueOrDefault(current,ChainRouting.Boundary(p));
                Edit(d=>ChainRouting.SetEnabled(d,false,split,nativeChainModes.GetValueOrDefault(current,0)));
            }
            Render();
        });
    }

    void RenderChainControls()
    {
        enableChain.Visibility=Visibility.Visible;
        enableChain.IsEnabled=current!=null;
        chainOptions.Content=null;
        chainOptions.Visibility=Visibility.Collapsed;
        if(current==null){enableChain.IsChecked=false;return;}

        var doc=current.Document;
        bool on=ChainRouting.Enabled(doc.FxLoopMode);
        enableChain.IsChecked=on;
        if(doc.FxLoopMode is 0 or 1)nativeChainModes[current]=doc.FxLoopMode;
        if(ChainRouting.Extended(doc.FxLoopMode))chainSplits[current]=ChainRouting.Boundary(doc);
        if(!on)return;

        chainOptions.Visibility=Visibility.Visible;
        var panel=new StackPanel{Margin=new Thickness(4,2,4,4),IsEnabled=true};
        var sound=new WrapPanel{VerticalAlignment=VerticalAlignment.Center};
        panel.Children.Add(sound);

        if(!ChainRouting.Independent(doc))
            panel.Children.Add(Text(ChainText("Legacy BLEND · the first BLEND edit separates it from VOL.","BLEND heredado · el primer cambio de BLEND lo separa de VOL."),10,brush:Muted));

        void Parameter(string label,double min,double max,double value,Action<PresetDocument,double> set,double reset)
        {
            var group=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,18,4),VerticalAlignment=VerticalAlignment.Center};
            var title=Text(label,11);title.Width=48;group.Children.Add(title);
            var slider=new Slider{Minimum=min,Maximum=max,Value=value,TickFrequency=1,IsSnapToTickEnabled=true,Width=160,Tag="chain-"+label,Cursor=Cursors.Hand};
            ToolTipService.SetShowOnDisabled(slider,true);
            var number=Text(((int)value).ToString(),12);number.Width=30;number.Margin=new Thickness(7,0,0,0);
            slider.ValueChanged+=(_,e)=>number.Text=((int)e.NewValue).ToString();
            slider.PreviewMouseDoubleClick+=(_,e)=>{slider.Value=reset;e.Handled=true;};
            AddDelayedSlider(slider,v=>Edit(d=>set(d,v)),suppress:()=>deviceSnapshotUpdating);
            group.Children.Add(slider);group.Children.Add(number);sound.Children.Add(group);
        }

        Parameter("BLEND",0,100,Math.Clamp(ChainRouting.Blend(doc),0,100),(d,v)=>ChainRouting.SetBlend(d,(float)v),50);
        var alignment=ChainRouting.Alignment(doc);
        Parameter("ALIGN",-32,32,alignment.frames,(d,v)=>ChainRouting.SetAlignment(d,(int)v,ChainRouting.Alignment(d).inverted),0);

        var phase=new CheckBox{Content="PHASE B · invert",IsChecked=alignment.inverted,Style=(Style)FindResource("ChainToggle"),FontSize=11,Margin=new Thickness(0,0,16,4),Tag="chain-phase",Cursor=Cursors.Hand,VerticalAlignment=VerticalAlignment.Center};
        ToolTipService.SetShowOnDisabled(phase,true);
        phase.Click+=(_,_)=>Guard(()=>Edit(d=>ChainRouting.SetAlignment(d,ChainRouting.Alignment(d).frames,phase.IsChecked==true)));
        sound.Children.Add(phase);

        // Control claro de Punto de División (SPLIT A/B)
        int currentSplit=ChainRouting.Boundary(doc);
        var splitGroup=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,14,4),VerticalAlignment=VerticalAlignment.Center};
        var splitLabel=Text("SPLIT A/B",11);splitLabel.Margin=new Thickness(0,0,8,0);splitLabel.VerticalAlignment=VerticalAlignment.Center;
        splitGroup.Children.Add(splitLabel);

        var splitPrev=new Button{Content="<",Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,0,4,0),Cursor=Cursors.Hand,ToolTip=ChainText("Move split point left","Mover punto de división a la izquierda")};
        ToolTipService.SetShowOnDisabled(splitPrev,true);
        var splitNumber=Text(currentSplit.ToString(),12,FontWeights.Bold,Accent);
        splitNumber.Width=24;splitNumber.TextAlignment=TextAlignment.Center;splitNumber.VerticalAlignment=VerticalAlignment.Center;
        var splitNext=new Button{Content=">",Padding=new Thickness(8,4,8,4),Margin=new Thickness(4,0,0,0),Cursor=Cursors.Hand,ToolTip=ChainText("Move split point right","Mover punto de división a la derecha")};
        ToolTipService.SetShowOnDisabled(splitNext,true);

        splitPrev.Click+=(_,_)=>Guard(()=>
        {
            if(currentSplit>1)
            {
                int nextSplit=currentSplit-1;
                chainSplits[current]=nextSplit;
                Edit(d=>ChainRouting.SetEnabled(d,true,nextSplit,nativeChainModes.GetValueOrDefault(current,0)));
                Render();
            }
        });
        splitNext.Click+=(_,_)=>Guard(()=>
        {
            if(currentSplit<10)
            {
                int nextSplit=currentSplit+1;
                chainSplits[current]=nextSplit;
                Edit(d=>ChainRouting.SetEnabled(d,true,nextSplit,nativeChainModes.GetValueOrDefault(current,0)));
                Render();
            }
        });

        splitGroup.Children.Add(splitPrev);
        splitGroup.Children.Add(splitNumber);
        splitGroup.Children.Add(splitNext);

        var autoDistribute=new Button
        {
            Content=ChainText("Auto A/B","Auto A/B"),
            Padding=new Thickness(8,4,8,4),
            Margin=new Thickness(6,0,0,0),
            Cursor=Cursors.Hand,
            ToolTip=ChainText("Balance pedals automatically: 4 in IN, 2 in Branch A, 2 in Branch B, 3 in OUT.","Distribuye los bloques automáticamente: 4 en Entrada, 2 en Rama A, 2 en Rama B y 3 en Salida.")
        };
        ToolTipService.SetShowOnDisabled(autoDistribute,true);
        autoDistribute.Click+=(_,_)=>Guard(()=>
        {
            if(current==null)return;
            chainSplits[current]=6;
            Edit(d=>
            {
                var c=Mx42ChainConfig.GetOrDefault(d);
                c=c with{Start=4,Split=6,End=8};
                d.SetCustomRouting(0x80|6,4,6,8);
                c.Write(d);
            });
            Render();
        });
        splitGroup.Children.Add(autoDistribute);
        sound.Children.Add(splitGroup);

        // Botón destacado: "Aplicar Cadena al Pedal" / "Sync Chain to GP-200"
        var syncButton=new Button
        {
            Content=ChainText("⚡ Sync Chain to GP-200","⚡ Sincronizar Cadena en Pedal"),
            Background=Brush("#168F8B"),
            Foreground=Brushes.White,
            FontWeight=FontWeights.SemiBold,
            Padding=new Thickness(12,6,12,6),
            Margin=new Thickness(8,0,0,4),
            Cursor=Cursors.Hand,
            VerticalAlignment=VerticalAlignment.Center
        };
        ToolTipService.SetShowOnDisabled(syncButton,true);
        syncButton.ToolTip=ChainText("Writes and activates the current dual chain topology directly on the GP-200 pedal.","Escribe y activa la topología dual A/B en el preset activo de la pedalera.");
        syncButton.IsEnabled=live!=null&&!liveBusy&&!loadingLibrary;
        syncButton.Click+=async(_,_)=>await SyncChainToDevice();
        sound.Children.Add(syncButton);

        panel.ToolTip=ChainText("BLEND: 0 = A, 50 = centre, 100 = B. ALIGN: + delays A / − delays B (stereo samples). Drag blocks between IN, A, B and OUT to change routing.","BLEND: 0 = A, 50 = centro, 100 = B. ALIGN: + retrasa A / − retrasa B (muestras estéreo). Arrastra bloques entre IN, A, B y OUT para cambiar las rutas.");
        chainOptions.Content=panel;
    }

    void UpdateChainInteraction()
    {
        enableChain.Visibility=Visibility.Visible;
        enableChain.IsEnabled=current!=null;
        if(chainOptions.Content is StackPanel panel)
            panel.IsEnabled=enableChain.IsChecked==true;
    }
}
