using System.Buffers.Binary;
using System.Diagnostics;

namespace Gp200.Replica;

internal sealed record NamToCloResult(bool Success, string? CloPath, CloDocument? Document, string Message);

internal static class NamToCloBridgeService
{
    internal static readonly string[] CandidateDirectories =
    [
        Path.Combine(AppContext.BaseDirectory, "Native"),
        Path.Combine(AppContext.BaseDirectory, "Converter"),
        Path.Combine(AppContext.BaseDirectory, "Tools"),
        AppContext.BaseDirectory
    ];

    static readonly string[] CandidateExecutables =
    [
        "namtoclo.exe",
        "nam_to_clo.exe",
        "NamToClo.exe",
        "namtoclo",
        "nam_to_clo"
    ];

    internal static string? FindConverterExecutable()
    {
        foreach (var dir in CandidateDirectories)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var exe in CandidateExecutables)
            {
                string path = Path.Combine(dir, exe);
                if (File.Exists(path)) return path;
            }
        }

        // Check system PATH
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            foreach (var part in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var exe in CandidateExecutables)
                {
                    try
                    {
                        string p = Path.Combine(part, exe);
                        if (File.Exists(p)) return p;
                    }
                    catch { }
                }
            }
        }

        return null;
    }

    internal static bool IsConverterAvailable => FindConverterExecutable() != null;

    internal static async Task<NamToCloResult> ConvertAsync(string namFilePath, string? outputCloPath = null, CancellationToken token = default)
    {
        if (!File.Exists(namFilePath))
            return new(false, null, null, $"Source NAM file not found: {namFilePath}");

        string targetClo = outputCloPath ?? Path.ChangeExtension(namFilePath, ".clo");
        string? converter = FindConverterExecutable();

        if (converter != null)
        {
            try
            {
                var psi = new ProcessStartInfo(converter)
                {
                    Arguments = $"\"{namFilePath}\" \"{targetClo}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                    return new(false, null, null, "Failed to start NamToClo process.");

                await process.WaitForExitAsync(token);
                if (process.ExitCode == 0 && File.Exists(targetClo))
                {
                    byte[] cloBytes = await File.ReadAllBytesAsync(targetClo, token);
                    var doc = new CloDocument(cloBytes);
                    return new(true, targetClo, doc, "Conversion completed successfully via NamToClo.");
                }

                string err = await process.StandardError.ReadToEndAsync(token);
                return new(false, null, null, $"NamToClo conversion failed (exit code {process.ExitCode}): {err}");
            }
            catch (Exception ex)
            {
                return new(false, null, null, $"Error running NamToClo: {ex.Message}");
            }
        }

        // Check if a pre-existing .clo file exists alongside the .nam file
        if (File.Exists(targetClo))
        {
            try
            {
                byte[] cloBytes = await File.ReadAllBytesAsync(targetClo, token);
                var doc = new CloDocument(cloBytes);
                return new(true, targetClo, doc, "Found existing valid .clo matching NAM model.");
            }
            catch (Exception ex)
            {
                return new(false, null, null, $"Matching .clo exists but is invalid: {ex.Message}");
            }
        }

        return new(false, null, null,
            "NamToClo converter executable not found. Place 'namtoclo.exe' in 'Tools/' or 'Native/' or load NAM directly via MX42 Nam Service.");
    }

    /// <summary>Generates a valid GP-200 A128/B1024 VTSI CLO template (exact 8840 bytes) for testing and offline modeling.</summary>
    internal static CloDocument CreateSyntheticClo(string name = "SYNTH_SNAP")
    {
        byte[] bytes = new byte[0x2288]; // 8840 bytes
        "VTSI"u8.CopyTo(bytes.AsSpan(0, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0x1288);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x14), 0x1200);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x78), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x7c), 128);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x80), 128);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x84), 1024);

        // Fill non-zero valid finite floats/doubles
        for (int at = 0x18; at < 0x68; at += 8)
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(at), 1.0);
        for (int at = 0x68; at < 0x78; at += 4)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at), 1.0f);
        for (int at = 0x88; at < 0x1288; at += 4)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at), 0.001f);

        // Calculate CRC16 over bytes 12..0x1288
        ushort crc = CloDocument.Crc(bytes.AsSpan(12, 0x1288 - 12));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(8), crc);

        return new CloDocument(bytes);
    }
}

