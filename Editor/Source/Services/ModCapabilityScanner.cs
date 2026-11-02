namespace Gp200.Replica;

public sealed record DetectedModInfo(string Name, string Description, bool IsActive, string Origin);

public sealed record SystemCapabilitiesReport(
    bool Connected,
    string ProfileName,
    bool DualChainUsb,
    bool ScenesUsb,
    bool Sfu1FootswitchProtocol,
    bool NativeNamLoader,
    IReadOnlyList<DetectedModInfo> DetectedMods,
    string Summary
);

public static class ModCapabilityScanner
{
    public static async Task<SystemCapabilitiesReport> ScanLiveDeviceAsync()
    {
        bool connected = DeviceSession.Current.Connected || ReadOnlyMidi.IsDeviceAvailable();
        bool isMx42 = CatalogRuntime.Current.IsMx42;

        bool dualChain = isMx42 && Mx42Integration.Supports(Mx42Capability.Chain);
        bool scenes = isMx42 && Mx42Integration.Supports(Mx42Capability.SceneSnapshot);
        bool namLoader = isMx42 && Mx42Integration.Supports(Mx42Capability.NamUpload);

        bool sfu1 = false;
        if (connected)
        {
            try
            {
                var table = await SceneFootswitchProtocol.ReadAsync();
                sfu1 = table.Status == 0;
            }
            catch
            {
                sfu1 = false;
            }
        }

        var mods = new List<DetectedModInfo>();
        if (dualChain)
            mods.Add(new("MX42 Dual Chain A/B USB", "Native realtime dual-path routing & BLEND/ALIGN engine", true, "Goaltoday MX42"));
        if (scenes)
            mods.Add(new("MX42 Scenes Protocol", "4 Scene snapshots per preset with instant recall", true, "Goaltoday MX42"));
        if (namLoader)
            mods.Add(new("MX42 Native NAM32 Loader", "Direct Neural Amp Modeler model upload to hardware slots", true, "Goaltoday MX42"));
        if (sfu1)
            mods.Add(new("SFU1 Footswitch Protocol", "Custom footswitch and scene hardware assignments", true, "Community SFU1"));

        string summary = connected
            ? $"Pedal GP-200 conectado · Perfil {(isMx42 ? "Custom MX42" : "Stock")} · {mods.Count} extensiones activas"
            : "Pedal GP-200 no conectado (modo offline).";

        return new(connected, CatalogRuntime.Current.Profile.ToString(), dualChain, scenes, sfu1, namLoader, mods, summary);
    }

    public static IReadOnlyList<DetectedModInfo> DetectFirmwareMods(ReadOnlySpan<byte> firmwareData)
    {
        var list = new List<DetectedModInfo>();
        if (firmwareData.Length < 96) return list;

        // These byte patterns are heuristic evidence only, not proof of installed or active behavior.
        // 1. Fast Preset Switch
        if (ContainsAsciiPattern(firmwareData, "FAST_SWITCH") || ContainsBytePattern(firmwareData, [0x46, 0x50, 0x53, 0x57]))
            list.Add(new("Fast Preset Switch", "Possible static marker found; this does not confirm the mod is installed or active.", false, "tntexplosivesltd · static marker"));

        // 2. Strobe Tuner mod
        if (ContainsAsciiPattern(firmwareData, "STROBE_TUNER") || ContainsAsciiPattern(firmwareData, "STRB"))
            list.Add(new("Strobe Tuner", "Possible static marker found; this does not confirm the mod is installed or active.", false, "tntexplosivesltd · static marker"));

        // 3. Carry-Over Tap Tempo
        if (ContainsAsciiPattern(firmwareData, "CARRY_OVER") || ContainsAsciiPattern(firmwareData, "TAP_CARRY"))
            list.Add(new("Carry-Over Tap Tempo", "Possible static marker found; this does not confirm the mod is installed or active.", false, "Goaltoday · static marker"));

        // 4. Dynamic Footswitch Labels
        if (ContainsAsciiPattern(firmwareData, "DYN_FS") || ContainsAsciiPattern(firmwareData, "SFU1"))
            list.Add(new("Dynamic Footswitch Protocol (SFU1)", "Possible static marker found; this does not confirm the protocol is installed or active.", false, "Community · static marker"));

        // 5. MX42 Extensions
        if (ContainsAsciiPattern(firmwareData, "MX42"))
            list.Add(new("MX42 Custom Extensions", "Possible MX42 marker found; this does not establish firmware identity or active capabilities.", false, "Goaltoday · static marker"));

        return list;
    }

    static bool ContainsAsciiPattern(ReadOnlySpan<byte> data, string ascii)
    {
        ReadOnlySpan<byte> pattern = System.Text.Encoding.ASCII.GetBytes(ascii);
        return data.IndexOf(pattern) >= 0;
    }

    static bool ContainsBytePattern(ReadOnlySpan<byte> data, ReadOnlySpan<byte> pattern)
    {
        return data.IndexOf(pattern) >= 0;
    }
}
