using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace Gp200.Replica;

public sealed record DeviceResponse(ReadQuery Query,int RequestedSlot,string Transmitted,IReadOnlyList<string> Received,
    IReadOnlyList<WireRecord> Records,int Length,IReadOnlyList<string> CleanupErrors)
{
    public IReadOnlyList<WireRecord>? WireRecords {get;init;}
    public int? VerifiedActiveSlot {get;init;}
}

/// <summary>Four public typed reads plus an internal restricted active-preset edit path.
/// No MIDI reset, program change, persistent save or firmware packets.</summary>
public static class ReadOnlyMidi
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct InCaps{public ushort Manufacturer,Product;public uint Version;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Name;public uint Support;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct OutCaps{public ushort Manufacturer,Product;public uint Version;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Name;public ushort Technology,Voices,Notes,ChannelMask;public uint Support;}
    [StructLayout(LayoutKind.Sequential)]
    struct Header{public IntPtr Data;public uint Length,Recorded;public UIntPtr User;public uint Flags;public IntPtr Next;public UIntPtr Reserved;public uint Offset;[MarshalAs(UnmanagedType.ByValArray,SizeConst=8)]public UIntPtr[] ReservedArray;}
    delegate void Callback(IntPtr handle,uint message,UIntPtr instance,UIntPtr parameter,UIntPtr timestamp);
    const string Library="winmm.dll";
    [DllImport(Library)]static extern uint midiInGetNumDevs();
    [DllImport(Library)]static extern uint midiOutGetNumDevs();
    [DllImport(Library,CharSet=CharSet.Unicode)]static extern uint midiInGetDevCapsW(UIntPtr id,out InCaps caps,uint size);
    [DllImport(Library,CharSet=CharSet.Unicode)]static extern uint midiOutGetDevCapsW(UIntPtr id,out OutCaps caps,uint size);
    [DllImport(Library)]static extern uint midiInOpen(out IntPtr handle,uint id,Callback callback,UIntPtr instance,uint flags);
    [DllImport(Library)]static extern uint midiOutOpen(out IntPtr handle,uint id,UIntPtr callback,UIntPtr instance,uint flags);
    [DllImport(Library)]static extern uint midiInPrepareHeader(IntPtr h,IntPtr header,uint size);
    [DllImport(Library)]static extern uint midiInUnprepareHeader(IntPtr h,IntPtr header,uint size);
    [DllImport(Library)]static extern uint midiInAddBuffer(IntPtr h,IntPtr header,uint size);
    [DllImport(Library)]static extern uint midiInStart(IntPtr h);
    [DllImport(Library)]static extern uint midiInStop(IntPtr h);
    [DllImport(Library)]static extern uint midiInReset(IntPtr h);
    [DllImport(Library)]static extern uint midiInClose(IntPtr h);
    [DllImport(Library)]static extern uint midiOutPrepareHeader(IntPtr h,IntPtr header,uint size);
    [DllImport(Library)]static extern uint midiOutUnprepareHeader(IntPtr h,IntPtr header,uint size);
    [DllImport(Library)]static extern uint midiOutLongMsg(IntPtr h,IntPtr header,uint size);
    [DllImport(Library)]static extern uint midiOutClose(IntPtr h);
    static readonly SemaphoreSlim Gate=new(1,1);
    // Retain callback and allocations if a faulty driver refuses to release them.
    static readonly ConcurrentBag<object> Quarantine=new();
    static uint HeaderSize=checked((uint)Marshal.SizeOf<Header>());

    public static async Task<DeviceResponse> QueryAsync(ReadQuery query,int slot=0)
    {
        byte[] request=Protocol.ReadRequest(query,slot);
        await Gate.WaitAsync();try{return await Task.Run(()=>
        {
            if(query!=ReadQuery.ActivePreset)return Query(query,slot,request,match:records=>Matches(query,slot,records));
            if(Mx42Integration.Supports(Mx42Capability.CoherentActive))
            {
                // Capability belongs only to the reviewed foreground coherent-copy hook.
                var active=Query(query,slot,request,match:records=>Matches(query,slot,records));
                int actual=System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(active.Records.Single(r=>r.Type==2).Value);
                Mx42Policy.ObserveSnapshot(active.Records);return ResolveActivePosition(active,actual);
            }
            // The loaded preset can retain a stale internal slot in its general
            // and chain headers. The global CurrentPreset is the real position.
            // Bracket the read so a pedal press cannot attach another body to it.
            for(int attempt=0;attempt<3;attempt++)
            {
                DeviceResponse State()=>Query(ReadQuery.State,0,Protocol.ReadRequest(ReadQuery.State),match:r=>Matches(ReadQuery.State,0,r));
                int Position(DeviceResponse state)=>GlobalSnapshot.Parse(state.Records.Single(r=>r.Type==0x1007).Value).CurrentPreset;
                var before=State();var active=Query(query,slot,request,match:records=>Matches(query,slot,records));var after=State();
                if(Position(before)!=Position(after))continue;
                Mx42Policy.ObserveSnapshot(active.Records);return ResolveActivePosition(active,Position(after)) with{CleanupErrors=before.CleanupErrors.Concat(active.CleanupErrors).Concat(after.CleanupErrors).ToArray()};
            }
            throw new IOException("The active preset changed during the read. No edit was sent.");
        });}finally{Gate.Release();}
    }
    internal static DeviceResponse ResolveActivePosition(DeviceResponse response,int actualSlot)
    {
        if(actualSlot is <0 or >255)throw new InvalidDataException("Active preset position is outside the GP-200 library.");
        var records=response.Records.Select(r=>new WireRecord(r.Type,(byte[])r.Value.Clone())).ToArray();
        foreach(var r in records)
        {
            if(r.Type==0&&r.Value.Length==4)System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(r.Value.AsSpan(2),(ushort)actualSlot);
            if(r.Type==2&&r.Value.Length==88||r.Type==8&&r.Value.Length==16)System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(r.Value,(ushort)actualSlot);
        }
        return response with{Records=records,WireRecords=response.WireRecords??response.Records,VerifiedActiveSlot=actualSlot};
    }
    static bool Matches(ReadQuery query,int slot,IReadOnlyList<WireRecord> records)
    {
        if(query==ReadQuery.Identity)return records.Count==1&&records[0].Type==0x2001&&records[0].Value.Length==4;
        if(query==ReadQuery.State)return records.Any(r=>r.Type==0x1007&&r.Value.Length==72)&&records.Any(r=>r.Type==0x1004&&r.Value.Length==124);
        var context=records.SingleOrDefault(r=>r.Type==0&&r.Value.Length==4);var general=records.SingleOrDefault(r=>r.Type==2&&r.Value.Length==88);
        if(context==null||general==null||records.Count(r=>r.Type==20&&r.Value.Length==68)!=11)return false;
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(context.Value)==(query==ReadQuery.ActivePreset?0:1)&&
            (query!=ReadQuery.SavedPreset||System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(general.Value)==slot);
    }
    internal static async Task<DeviceResponse> ExchangeInternalAsync(byte command,IReadOnlyList<WireRecord> records,Func<IReadOnlyList<WireRecord>,bool>? match,int timeoutMs)
    {
        Mx42Policy.CheckWire(command,records);var frames=Protocol.Encode(command,records);await Gate.WaitAsync();
        bool integration=command==0x11&&records.Count==1&&records[0].Type==0x1009&&records[0].Value.Length>=32&&System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(records[0].Value)==0x584d&&System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(records[0].Value.AsSpan(4))==0x3145584d;
        try{return await Task.Run(()=>Query(ReadQuery.State,0,frames[0],match==null,frames,match,true,timeoutMs,integrationPacing:integration));}finally{Gate.Release();}
    }
    static void Check(uint result,string operation){if(result!=0)throw new IOException(UiText.T($"{operation}: error MIDI {result}. Cierra el editor oficial y otras aplicaciones que usen GP-200."));}
    
    public static bool IsValetonFamily(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return name.IndexOf("Valeton", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("GP-200", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("GP200", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("GP-100", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("GP100", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool IsDeviceAvailable()
    {
        try{_=FindInput();_=FindOutput();return true;}catch(IOException){return false;}
    }

    public static List<(uint id, string name)> GetInputDevices()
    {
        var devs = new List<(uint id, string name)>();
        uint count = midiInGetNumDevs();
        for (uint i = 0; i < count; i++) {
            try
            {
                uint res = midiInGetDevCapsW((UIntPtr)i, out var caps, (uint)Marshal.SizeOf<InCaps>());
                if (res == 0 && !string.IsNullOrWhiteSpace(caps.Name)) devs.Add((i, caps.Name.Trim()));
            }
            catch { /* Skip on device caps read exception */ }
        }
        return devs;
    }

    public static List<(uint id, string name)> GetOutputDevices()
    {
        var devs = new List<(uint id, string name)>();
        uint count = midiOutGetNumDevs();
        for (uint i = 0; i < count; i++) {
            try
            {
                uint res = midiOutGetDevCapsW((UIntPtr)i, out var caps, (uint)Marshal.SizeOf<OutCaps>());
                if (res == 0 && !string.IsNullOrWhiteSpace(caps.Name)) devs.Add((i, caps.Name.Trim()));
            }
            catch { /* Skip on device caps read exception */ }
        }
        return devs;
    }

    public static (uint id, string name) SelectDevice(List<(uint id, string name)> devs, bool isOutput)
    {
        if (devs.Count == 0)
            throw new IOException(UiText.T(isOutput ? "No se detectó ningún dispositivo MIDI de salida. Conecta tu pedalera." : "No se detectó ningún dispositivo MIDI de entrada. Conecta tu pedalera."));

        // Prioridad 0: Preferencia explícita guardada por el usuario en Ajustes
        try
        {
            var prefs = AppPreferences.Load();
            string? preferred = isOutput ? prefs.MidiOutputDevice : prefs.MidiInputDevice;
            if (!string.IsNullOrWhiteSpace(preferred))
            {
                var exact = devs.FirstOrDefault(d => string.Equals(d.name, preferred, StringComparison.OrdinalIgnoreCase));
                if (exact.name != null) return exact;
                var partial = devs.FirstOrDefault(d => d.name.IndexOf(preferred, StringComparison.OrdinalIgnoreCase) >= 0);
                if (partial.name != null) return partial;
            }
        }
        catch { }

        // Prioridad 1: Dispositivos de la familia Valeton/GP-200 que NO contengan "Subdevice"
        var match = devs.FirstOrDefault(d => IsValetonFamily(d.name) && d.name.IndexOf("Subdevice", StringComparison.OrdinalIgnoreCase) < 0);
        if (match.name != null) return match;

        // Prioridad 2: Si no hay endpoint sin "Subdevice", usar cualquier dispositivo de la familia Valeton/GP-200
        match = devs.FirstOrDefault(d => IsValetonFamily(d.name));
        if (match.name != null) return match;

        // Prioridad 3: Para salida, descartar sintetizadores virtuales de Windows (Microsoft GS Wavetable Synth / MIDI Mapper)
        if (isOutput)
        {
            var hardwareDev = devs.FirstOrDefault(d =>
                d.name.IndexOf("Microsoft GS", StringComparison.OrdinalIgnoreCase) < 0 &&
                d.name.IndexOf("Wavetable", StringComparison.OrdinalIgnoreCase) < 0 &&
                d.name.IndexOf("MIDI Mapper", StringComparison.OrdinalIgnoreCase) < 0);
            if (hardwareDev.name != null) return hardwareDev;
        }

        // Prioridad 4: Primer puerto disponible cuando el driver no identifica la pedalera por nombre.
        return devs[0];
    }

    static uint OpenMidiInWithRetry(out IntPtr handle, uint id, Callback callback, UIntPtr instance, uint flags, int maxAttempts = 5)
    {
        uint res = 0;
        handle = IntPtr.Zero;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            res = midiInOpen(out handle, id, callback, instance, flags);
            if (res == 0) return 0;
            if (res == 4 /* MMSYSERR_ALLOCATED */) Thread.Sleep(50);
            else break;
        }
        return res;
    }

    static uint OpenMidiOutWithRetry(out IntPtr handle, uint id, UIntPtr callback, UIntPtr instance, uint flags, int maxAttempts = 5)
    {
        uint res = 0;
        handle = IntPtr.Zero;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            res = midiOutOpen(out handle, id, callback, instance, flags);
            if (res == 0) return 0;
            if (res == 4 /* MMSYSERR_ALLOCATED */) Thread.Sleep(50);
            else break;
        }
        return res;
    }

    public static void SendRawSysEx(byte[] frame)
    {
        if (frame == null || frame.Length == 0 || !IsDeviceAvailable()) return;
        Gate.Wait();
        IntPtr output = IntPtr.Zero;
        IntPtr outData = IntPtr.Zero;
        IntPtr outHeader = IntPtr.Zero;
        try
        {
            uint outputId = FindOutput();
            if (OpenMidiOutWithRetry(out output, outputId, UIntPtr.Zero, UIntPtr.Zero, 0) != 0) return;
            outData = Marshal.AllocHGlobal(frame.Length);
            Marshal.Copy(frame, 0, outData, frame.Length);
            outHeader = Marshal.AllocHGlobal((int)HeaderSize);
            Marshal.StructureToPtr(new Header { Data = outData, Length = (uint)frame.Length, ReservedArray = new UIntPtr[8] }, outHeader, false);
            if (midiOutPrepareHeader(output, outHeader, HeaderSize) == 0)
            {
                midiOutLongMsg(output, outHeader, HeaderSize);
                var tx = Stopwatch.StartNew();
                while ((Marshal.PtrToStructure<Header>(outHeader).Flags & 1) == 0 && tx.ElapsedMilliseconds < 2000)
                    Thread.Sleep(2);
                midiOutUnprepareHeader(output, outHeader, HeaderSize);
            }
        }
        catch
        {
            // Silently absorb transient errors in fire-and-forget SysEx
        }
        finally
        {
            if (outHeader != IntPtr.Zero) Marshal.FreeHGlobal(outHeader);
            if (outData != IntPtr.Zero) Marshal.FreeHGlobal(outData);
            if (output != IntPtr.Zero) midiOutClose(output);
            Gate.Release();
        }
    }

    public static string GetPedalDiagnosticDetails()
    {
        var inDevs = GetInputDevices();
        var outDevs = GetOutputDevices();
        bool hasValetonIn = inDevs.Any(d => IsValetonFamily(d.name));
        bool hasValetonOut = outDevs.Any(d => IsValetonFamily(d.name));

        if (hasValetonIn && hasValetonOut)
        {
            return UiText.Language == "es"
                ? "Dispositivo GP-200 detectado en WinMM y listo para comunicarse."
                : "GP-200 device detected in WinMM and ready for communication.";
        }

        bool usbPnpRegistered = false;
        try
        {
            using var usbKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
            if (usbKey != null)
            {
                foreach (var sub in usbKey.GetSubKeyNames())
                {
                    if (sub.IndexOf("VID_84EF", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        usbPnpRegistered = true;
                        break;
                    }
                }
            }
        }
        catch { }

        var sb = new System.Text.StringBuilder();
        if (UiText.Language == "es")
        {
            sb.AppendLine("No se detectó la GP-200 en los puertos MIDI de Windows.");
            sb.AppendLine();
            sb.AppendLine("Diagnóstico del sistema:");
            if (inDevs.Count == 0 && outDevs.Count <= 1)
            {
                sb.AppendLine("• No hay dispositivos MIDI físicos activos en Windows.");
            }
            else
            {
                sb.AppendLine($"• Puertos MIDI de entrada detectados ({inDevs.Count}): " + (inDevs.Count > 0 ? string.Join(", ", inDevs.Select(d => d.name)) : "ninguno"));
                sb.AppendLine($"• Puertos MIDI de salida detectados ({outDevs.Count}): " + string.Join(", ", outDevs.Select(d => d.name)));
            }

            sb.AppendLine();
            if (usbPnpRegistered)
            {
                sb.AppendLine("• El controlador USB de Valeton (VID 84EF) está registrado en Windows, pero el pedal está desconectado físicamente, apagado o en reinicio.");
                sb.AppendLine("• Solución:");
                sb.AppendLine("  1. Comprueba que el cable USB esté conectado firmemente a un puerto USB directo del PC (no a un hub sin alimentación).");
                sb.AppendLine("  2. Asegúrate de que el interruptor de encendido de la GP-200 esté en ON.");
                sb.AppendLine("  3. Si la desconectaste recientemente, desconecta el cable USB, espera 5 segundos y vuelve a conectarlo.");
                sb.AppendLine("  4. Cierra el editor oficial de Valeton si está abierto.");
            }
            else
            {
                sb.AppendLine("• No se encontró el driver USB de Valeton (VID 84EF) registrado.");
                sb.AppendLine("• Solución: Instala el driver oficial USB/ASIO de Valeton GP-200 y conecta el cable USB.");
            }
        }
        else
        {
            sb.AppendLine("GP-200 was not detected in Windows MIDI ports.");
            sb.AppendLine();
            sb.AppendLine("System Diagnostics:");
            if (inDevs.Count == 0 && outDevs.Count <= 1)
            {
                sb.AppendLine("• No physical MIDI devices are active in Windows.");
            }
            else
            {
                sb.AppendLine($"• Input MIDI ports ({inDevs.Count}): " + (inDevs.Count > 0 ? string.Join(", ", inDevs.Select(d => d.name)) : "none"));
                sb.AppendLine($"• Output MIDI ports ({outDevs.Count}): " + string.Join(", ", outDevs.Select(d => d.name)));
            }

            sb.AppendLine();
            if (usbPnpRegistered)
            {
                sb.AppendLine("• Valeton USB driver (VID 84EF) is registered, but pedal is disconnected, powered off, or reset.");
                sb.AppendLine("• Action items:");
                sb.AppendLine("  1. Ensure USB cable is plugged into a direct PC USB port.");
                sb.AppendLine("  2. Ensure GP-200 power switch is ON.");
                sb.AppendLine("  3. Unplug USB cable, wait 5 seconds, and replug.");
                sb.AppendLine("  4. Close official Valeton software if running.");
            }
            else
            {
                sb.AppendLine("• Valeton USB driver (VID 84EF) is not registered.");
                sb.AppendLine("• Action: Install Valeton GP-200 USB/ASIO driver and plug in USB cable.");
            }
        }

        return sb.ToString().TrimEnd();
    }
    static uint FindInput()
    {
        var devs = GetInputDevices();
        if (devs.Count == 0) throw new IOException(UiText.T("No se detectó ningún dispositivo MIDI de entrada. Conecta tu pedalera."));
        return SelectDevice(devs, isOutput: false).id;
    }
    static uint FindOutput()
    {
        var devs = GetOutputDevices();
        if (devs.Count == 0) throw new IOException(UiText.T("No se detectó ningún dispositivo MIDI de salida. Conecta tu pedalera."));
        return SelectDevice(devs, isOutput: true).id;
    }
    static DeviceResponse Query(ReadQuery query,int slot,byte[] request,bool edit=false,IReadOnlyList<byte[]>? fragments=null,Func<IReadOnlyList<WireRecord>,bool>? match=null,bool generic=false,int timeoutMs=3000,bool integrationPacing=false)
    {
        var received=new ConcurrentQueue<byte[]>();var callbackErrors=new ConcurrentQueue<string>();
        Callback callback=(handle,message,instance,parameter,time)=>
        {
            if(message is not (0x3c4 or 0x3c6))return;
            try
            {
                var header=Marshal.PtrToStructure<Header>((IntPtr)parameter);
                if(header.Recorded==0)return;
                if(header.Recorded>header.Length){callbackErrors.Enqueue(UiText.T("El driver indicó datos fuera del buffer."));return;}
                var bytes=new byte[header.Recorded];
                Marshal.Copy(header.Data,bytes,0,bytes.Length);
                if(message==0x3c6)callbackErrors.Enqueue(UiText.T("Driver MIDI: error de recepción SysEx."));
                received.Enqueue(bytes);
            }
            catch(Exception e){callbackErrors.Enqueue(e.Message);}
        };
        var buffers=new List<(IntPtr Data,IntPtr Header,bool Prepared)>();var cleanup=new List<string>();
        IntPtr input=IntPtr.Zero,output=IntPtr.Zero,outData=IntPtr.Zero,outHeader=IntPtr.Zero;bool outPrepared=false;
        try
        {
            uint inputId=FindInput(),outputId=FindOutput();
            Check(OpenMidiInWithRetry(out input,inputId,callback,UIntPtr.Zero,0x30000),UiText.T("Abrir entrada"));
            for(int n=0;n<32;n++)
            {
                IntPtr data=Marshal.AllocHGlobal(16384),header=Marshal.AllocHGlobal((int)HeaderSize);buffers.Add((data,header,false));
                Marshal.StructureToPtr(new Header{Data=data,Length=16384,ReservedArray=new UIntPtr[8]},header,false);
                Check(midiInPrepareHeader(input,header,HeaderSize),UiText.T("Preparar recepción"));buffers[^1]=(data,header,true);Check(midiInAddBuffer(input,header,HeaderSize),UiText.T("Encolar recepción"));
            }
            Check(midiInStart(input),UiText.T("Iniciar recepción"));Check(OpenMidiOutWithRetry(out output,outputId,UIntPtr.Zero,UIntPtr.Zero,0),UiText.T("Abrir salida"));
            int packet=0;foreach(var frame in fragments??new[]{request})
            {
                outData=Marshal.AllocHGlobal(frame.Length);Marshal.Copy(frame,0,outData,frame.Length);outHeader=Marshal.AllocHGlobal((int)HeaderSize);
                Marshal.StructureToPtr(new Header{Data=outData,Length=(uint)frame.Length,ReservedArray=new UIntPtr[8]},outHeader,false);
                Check(midiOutPrepareHeader(output,outHeader,HeaderSize),UiText.T("Preparar mensaje"));outPrepared=true;Check(midiOutLongMsg(output,outHeader,HeaderSize),UiText.T("Enviar mensaje"));
                var tx=Stopwatch.StartNew();while((Marshal.PtrToStructure<Header>(outHeader).Flags&1)==0&&tx.ElapsedMilliseconds<3000)Thread.Sleep(2);
                if((Marshal.PtrToStructure<Header>(outHeader).Flags&1)==0)throw new IOException(UiText.T("El driver no confirmó el envío."));
                Check(midiOutUnprepareHeader(output,outHeader,HeaderSize),UiText.T("Liberar envío"));outPrepared=false;Marshal.FreeHGlobal(outHeader);Marshal.FreeHGlobal(outData);outHeader=outData=IntPtr.Zero;
                packet++;if(fragments?.Count>1)Thread.Sleep(5);
            }
            if(!integrationPacing&&fragments?.Count>1)Thread.Sleep(1000); // Native host transfer pacing, 0x5ae6d0.
            // Local bounded timeout; not claimed as a firmware timing requirement.
            var deadline=Stopwatch.StartNew();bool complete=false;
            while(deadline.ElapsedMilliseconds<timeoutMs)
            {
                Thread.Sleep(5);
                complete=CompletedMessages(received.ToArray()).Any(message=>match==null||match(Protocol.Records(Protocol.Reassemble(message))));
                if(complete||edit){if(edit)Thread.Sleep(5);break;}
            }
        }
        finally
        {
            void Note(string operation,uint result){if(result!=0)cleanup.Add($"{operation}: {result}");}
            if(input!=IntPtr.Zero){Note("stop input",midiInStop(input));Note("return input buffers",midiInReset(input));}
            foreach(var buffer in buffers)
            {
                uint result=buffer.Prepared?midiInUnprepareHeader(input,buffer.Header,HeaderSize):0;
                if(result==0){Marshal.FreeHGlobal(buffer.Header);Marshal.FreeHGlobal(buffer.Data);}else{Note("release input buffer",result);Quarantine.Add(buffer);Quarantine.Add(callback);}
            }
            if(input!=IntPtr.Zero){uint result=midiInClose(input);Note("close input",result);if(result!=0)Quarantine.Add(callback);}
            uint outResult=outPrepared?midiOutUnprepareHeader(output,outHeader,HeaderSize):0;
            if(outResult==0){if(outHeader!=IntPtr.Zero)Marshal.FreeHGlobal(outHeader);if(outData!=IntPtr.Zero)Marshal.FreeHGlobal(outData);}else{Note("release output buffer",outResult);Quarantine.Add((outData,outHeader));}
            if(output!=IntPtr.Zero)Note("close output",midiOutClose(output));
            GC.KeepAlive(callback);
        }
        if(callbackErrors.TryPeek(out var error))throw new IOException(error);
        byte[][] frames=received.ToArray();
        string transmitted=string.Join(UiText.T(" "),(fragments??new[]{request}).Select(Convert.ToHexString));
        if(edit)return new(query,slot,transmitted,frames.Select(Convert.ToHexString).ToArray(),[],0,cleanup);
        var selected=CompletedMessages(frames).LastOrDefault(message=>match==null||match(Protocol.Records(Protocol.Reassemble(message))))??throw new IOException(UiText.T("No llegó la respuesta esperada de GP-200."));
        byte[] payload=Protocol.Reassemble(selected);
        var records=Protocol.Records(payload);
        if(generic)return new(query,slot,transmitted,frames.Select(Convert.ToHexString).ToArray(),records,payload.Length,cleanup);
        // A response is identified by its logical content, never by length alone.
        if(query==ReadQuery.Identity&&(records.Count!=1||records[0].Type!=0x2001||records[0].Value.Length!=4))throw new InvalidDataException(UiText.T("La respuesta no corresponde a identidad."));
        if(query==ReadQuery.State&&!records.Any(r=>r.Type==0x1007))throw new InvalidDataException(UiText.T("La respuesta no contiene estado global."));
        if(query is ReadQuery.ActivePreset or ReadQuery.SavedPreset)
        {
            var context=records.Where(r=>r.Type==0&&r.Value.Length==4).ToArray();
            var general=records.Where(r=>r.Type==2&&r.Value.Length>=88).ToArray();
            if(context.Length!=1||general.Length!=1)throw new InvalidDataException(UiText.T("Respuesta de preset ambigua."));
            ushort mode=System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(context[0].Value);
            if(mode!=(query==ReadQuery.ActivePreset?0:1))throw new InvalidDataException(UiText.T("Contexto de preset distinto del solicitado."));
            if(query==ReadQuery.SavedPreset&&System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(general[0].Value)!=slot)throw new InvalidDataException(UiText.T("La respuesta pertenece a otro preset guardado."));
        }
        return new(query,slot,Convert.ToHexString(request),frames.Select(Convert.ToHexString).ToArray(),records,payload.Length,cleanup);
    }
    internal static List<byte[][]> CompletedMessages(IEnumerable<byte[]> chunks)
    {
        var completed=new List<byte[][]>();var frame=new List<byte>();var pending=new List<byte[]>();int expected=0;bool inside=false;
        foreach(byte value in chunks.SelectMany(c=>c))
        {
            if(value>=0xf8)continue; // MIDI realtime may be interleaved inside SysEx.
            if(value==0xf0){frame.Clear();frame.Add(value);inside=true;continue;}
            if(!inside)continue;frame.Add(value);if(value!=0xf7)continue;inside=false;
            try
            {
                byte[] bytes=frame.ToArray();var part=Protocol.Decode(bytes);
                if(part.Offset==0){pending.Clear();expected=0;}
                if(part.Offset!=expected){pending.Clear();expected=0;continue;}
                pending.Add(bytes);expected+=part.Payload.Length;
                if(expected==part.Total){_=Protocol.Reassemble(pending);completed.Add(pending.ToArray());pending.Clear();expected=0;}
            }
            catch(InvalidDataException){pending.Clear();expected=0;}
        }
        return completed;
    }
}
