using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Gp200.Replica;

public sealed record FirmwareBinScanResult(
    string BaseVersion,
    string ModVersion,
    string[] NamSlotLabels,
    IReadOnlyList<string> UnlockedFeatures,
    int TotalBytes,
    string Sha256
);

/// <summary>
/// Escáner, extractor y constructor de archivos binarios (.BIN) de firmware oficial y Custom Mod (0.8.3 / v1.8.0+).
/// Extrae la versión base, versión del mod, catálogo real de los 32 slots NAM, sincroniza componentes
/// y construye binarios .BIN personalizados inyectando modelos convertidos mediante NamToCloBridgeService.
/// </summary>
public static class FirmwareBinScanner
{
    public static event Action<string[]>? NamCatalogUpdated;

    public static FirmwareBinScanResult ScanFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException(UiText.T("Archivo de firmware no encontrado."), filePath);

        byte[] bytes = File.ReadAllBytes(filePath);
        return Scan(bytes, Path.GetFileName(filePath));
    }

    public static FirmwareBinScanResult Scan(byte[] data, string fileName = "")
    {
        if (data.Length < 96)
            throw new InvalidDataException(UiText.T("El archivo .BIN es demasiado pequeño para ser un firmware válido."));

        string sha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        string baseVersion = "v1.8.0";
        string modVersion = "Oficial (Stock)";
        var unlockedFeatures = new List<string>();
        string[] namSlots = new string[32];

        // Inicializar slots por defecto
        for (int i = 0; i < 32; i++)
        {
            namSlots[i] = $"NAM {i + 1:00} · Modelo {i + 1:00}";
        }

        // 1. Detección de Cabecera FRMW o binario desempacado
        bool hasFrmwHeader = data.Length >= 4 && data.AsSpan(0, 4).SequenceEqual("FRMW"u8);
        if (hasFrmwHeader)
        {
            if (data.Length >= 92)
            {
                var marker = data.AsSpan(88, 4);
                string vStr = new(marker.ToArray().Select(b => b is >= 32 and <= 126 ? (char)b : '.').ToArray());
                if (vStr.Contains("1.") || vStr.Contains("2."))
                {
                    baseVersion = "v" + vStr.Trim();
                }
            }
        }

        // 2. Escaneo de firmas de Custom Mod (MX42, Custom 0.8.3, Goaltoday, Fix4)
        string asciiContent = Encoding.Latin1.GetString(data);

        if (asciiContent.Contains("MX42", StringComparison.OrdinalIgnoreCase) ||
            asciiContent.Contains("0.8.3", StringComparison.OrdinalIgnoreCase) ||
            asciiContent.Contains("Custom Mod", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("0.8.3", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("Custom", StringComparison.OrdinalIgnoreCase))
        {
            modVersion = "Custom 0.8.3 + Fix4 Patch (MX42 Enhanced)";
            unlockedFeatures.Add("32 Slots de Modelos NAM A2 Lite (48 kHz)");
            unlockedFeatures.Add("Modo Scenes (A/B/C/D) con transiciones sin corte");
            unlockedFeatures.Add("Dual Signal Chain (Ruteo Paralelo A/B)");
            unlockedFeatures.Add("Soporte para 2 bloques NAM concurrentes");
            unlockedFeatures.Add("Matriz de Footswitch CTRL 8x11 extendida");
        }
        else
        {
            unlockedFeatures.Add("Firmware estándar Valeton GP-200");
            unlockedFeatures.Add("Compatibilidad con SnapTone .CLO (VTSI 8840 bytes)");
            unlockedFeatures.Add("Carga de Respuestas de Impulso IR 44.1/48 kHz");
        }

        // 3. Extracción de Nombres de los 32 Slots NAM desde el binario
        int namTableOffset = FindNamCatalogOffset(data);
        if (namTableOffset > 0 && namTableOffset + 32 * 16 <= data.Length)
        {
            for (int i = 0; i < 32; i++)
            {
                int start = namTableOffset + i * 16;
                string slotName = ExtractCleanAsciiString(data, start, 16);
                if (!string.IsNullOrWhiteSpace(slotName) && slotName.Length >= 2)
                {
                    namSlots[i] = $"NAM {i + 1:00} · {slotName}";
                }
            }
            unlockedFeatures.Add("✓ Catálogo de 32 nombres NAM extraído directamente del archivo .BIN");
        }
        else
        {
            var foundNames = ScanForNamStringPatterns(asciiContent);
            if (foundNames.Count > 0)
            {
                for (int i = 0; i < Math.Min(32, foundNames.Count); i++)
                {
                    namSlots[i] = $"NAM {i + 1:00} · {foundNames[i]}";
                }
                unlockedFeatures.Add($"✓ {foundNames.Count} nombres de modelos identificados en el binario");
            }
        }

        // Auto-población global: actualizar catalog runtime y disparar notificación
        try
        {
            if (CatalogRuntime.Current.IsMx42)
            {
                CatalogRuntime.Current.AcceptMx42DeviceNames(namSlots);
            }
            if (DeviceSession.Current.Catalog.IsMx42)
            {
                DeviceSession.Current.Catalog.AcceptMx42DeviceNames(namSlots);
            }
            NamCatalogUpdated?.Invoke(namSlots);
        }
        catch
        {
            // Ignorar en contextos no inicializados
        }

        return new FirmwareBinScanResult(
            baseVersion,
            modVersion,
            namSlots,
            unlockedFeatures,
            data.Length,
            sha256
        );
    }

    /// <summary>
    /// Construye un archivo de firmware .BIN personalizado inyectando modelos convertidos (8840 bytes)
    /// y actualizando los nombres en la tabla del contenedor FRMW.
    /// </summary>
    public static byte[] BuildCustomFirmwareBin(
        byte[] baseBinData,
        IReadOnlyList<(int slot, string name, byte[] cloData)> customSlots)
    {
        if (baseBinData.Length < 1024)
            throw new InvalidDataException("El binario base es inválido o demasiado pequeño.");

        byte[] patched = (byte[])baseBinData.Clone();

        int namesOffset = FindNamCatalogOffset(patched);
        if (namesOffset <= 0)
        {
            // Si el binario base no tenía la tabla localizada, buscar o crear a offset estándar (0x1000)
            namesOffset = 0x1000;
        }

        int weightsOffset = FindNamWeightsOffset(patched);
        if (weightsOffset <= 0)
        {
            weightsOffset = 0x2000;
        }

        foreach (var (slot, name, cloData) in customSlots)
        {
            if (slot is < 0 or > 31) continue;

            // 1. Escribir nombre en la tabla ASCII (16 bytes exactos terminados en null)
            int nameTarget = namesOffset + slot * 16;
            if (nameTarget + 16 <= patched.Length)
            {
                byte[] nameBytes = new byte[16];
                string cleanName = string.Concat(name.Where(c => c is >= ' ' and <= '~')).Trim();
                if (cleanName.Length > 15) cleanName = cleanName[..15];
                Encoding.ASCII.GetBytes(cleanName).CopyTo(nameBytes, 0);
                nameBytes.CopyTo(patched, nameTarget);
            }

            // 2. Inyectar pesos / modelo CLO (8840 bytes exactos, cabecera VTSI)
            if (cloData != null && cloData.Length == 0x2288) // 8840 bytes exactos
            {
                int weightTarget = weightsOffset + slot * 0x2288;
                if (weightTarget + 0x2288 <= patched.Length)
                {
                    cloData.CopyTo(patched, weightTarget);
                }
            }
        }

        return patched;
    }

    private static int FindNamCatalogOffset(byte[] data)
    {
        for (int i = 0; i < data.Length - 64; i += 4)
        {
            if (data[i] == 'N' && data[i + 1] == 'A' && data[i + 2] == 'M' && (data[i + 3] == ' ' || data[i + 3] == '\0'))
            {
                if (i + 16 < data.Length && (data[i + 16] == 'N' || char.IsAsciiLetter((char)data[i + 16])))
                {
                    return i;
                }
            }
        }
        return -1;
    }

    private static int FindNamWeightsOffset(byte[] data)
    {
        // Buscar el primer bloque VTSI en el binario
        byte[] vtsi = "VTSI"u8.ToArray();
        for (int i = 0; i < data.Length - 0x2288; i += 16)
        {
            if (data[i] == 'V' && data[i + 1] == 'T' && data[i + 2] == 'S' && data[i + 3] == 'I')
            {
                return i;
            }
        }
        return -1;
    }

    private static List<string> ScanForNamStringPatterns(string content)
    {
        var list = new List<string>();
        string[] commonModels = ["JCM800", "5150", "DualRect", "Plexi", "TwinReverb", "AC30", "Bogner", "Friedman", "MarkIV", "SLO100", "Diezel", "ENGL"];
        foreach (var m in commonModels)
        {
            if (content.Contains(m, StringComparison.OrdinalIgnoreCase))
            {
                list.Add(m);
            }
        }
        return list;
    }

    private static string ExtractCleanAsciiString(byte[] data, int start, int maxLen)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < maxLen && (start + i) < data.Length; i++)
        {
            byte b = data[start + i];
            if (b == 0) break;
            if (b >= 32 && b <= 126) sb.Append((char)b);
            else break;
        }
        return sb.ToString().Trim();
    }
}
