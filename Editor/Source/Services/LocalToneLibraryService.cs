namespace Gp200.Replica;

internal enum ToneCategory { UserIr, NamModel, SnapTone }

internal sealed record ToneFileItem(
    string Name,
    string FullPath,
    ToneCategory Category,
    long FileSizeBytes,
    string Details
);

internal static class LocalToneLibraryService
{
    static readonly List<ToneFileItem> Cache = [];
    static DateTime lastScanUtc = DateTime.MinValue;

    internal static IReadOnlyList<ToneFileItem> Items => Cache;

    internal static IReadOnlyList<string> DefaultDirectories =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Valeton GP-200", "Tones"),
        Path.Combine(AppPaths.Root, "Tones"),
        Path.Combine(AppContext.BaseDirectory, "Tones"),
        Path.Combine(AppContext.BaseDirectory, "Data")
    ];

    internal static IReadOnlyList<ToneFileItem> Scan(string? extraFolder = null)
    {
        var dirs = new HashSet<string>(DefaultDirectories, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(extraFolder) && Directory.Exists(extraFolder))
            dirs.Add(extraFolder);

        var list = new List<ToneFileItem>();
        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                var files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    string name = Path.GetFileNameWithoutExtension(file);
                    var fi = new FileInfo(file);

                    if (ext == ".wav")
                    {
                        list.Add(new ToneFileItem(name, file, ToneCategory.UserIr, fi.Length, "WAV Impulse Response (IR)"));
                    }
                    else if (ext == ".nam")
                    {
                        list.Add(new ToneFileItem(name, file, ToneCategory.NamModel, fi.Length, "Neural Amp Modeler (NAM)"));
                    }
                    else if (ext == ".clo")
                    {
                        bool validClo = false;
                        if (fi.Length == 0x2288)
                        {
                            try
                            {
                                byte[] bytes = File.ReadAllBytes(file);
                                if (bytes.AsSpan(0, 4).SequenceEqual("VTSI"u8))
                                    validClo = true;
                            }
                            catch { }
                        }
                        list.Add(new ToneFileItem(name, file, ToneCategory.SnapTone, fi.Length, validClo ? "Valeton SnapTone / Tone Match (VTSI)" : "SnapTone (.clo)"));
                    }
                }
            }
            catch { }
        }

        Cache.Clear();
        Cache.AddRange(list);
        lastScanUtc = DateTime.UtcNow;
        return Cache;
    }

    internal static string GetSummaryForAi()
    {
        if (Cache.Count == 0 && DateTime.UtcNow - lastScanUtc > TimeSpan.FromMinutes(5))
            Scan();

        if (Cache.Count == 0)
            return "No local tone library files (.wav, .nam, .clo) found in user directories.";

        var irs = Cache.Where(i => i.Category == ToneCategory.UserIr).Select(i => i.Name).Take(20).ToArray();
        var nams = Cache.Where(i => i.Category == ToneCategory.NamModel).Select(i => i.Name).Take(20).ToArray();
        var clos = Cache.Where(i => i.Category == ToneCategory.SnapTone).Select(i => i.Name).Take(20).ToArray();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("User's local tone inventory on PC:");
        if (irs.Length > 0) sb.AppendLine($"  - Local IRs ({Cache.Count(i => i.Category == ToneCategory.UserIr)} total): {string.Join(", ", irs)}");
        if (nams.Length > 0) sb.AppendLine($"  - Local NAM models ({Cache.Count(i => i.Category == ToneCategory.NamModel)} total): {string.Join(", ", nams)}");
        if (clos.Length > 0) sb.AppendLine($"  - Local SnapTone .clo ({Cache.Count(i => i.Category == ToneCategory.SnapTone)} total): {string.Join(", ", clos)}");
        return sb.ToString().TrimEnd();
    }
}

