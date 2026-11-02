using System.IO.Compression;
using System.Globalization;

namespace Gp200.Replica;

internal sealed record ArchivedPreset(string Name, byte[] Bytes, PresetDocument Document);

internal static class PresetArchive
{
    const int MaxPresets = 256;
    const int MaxPresetBytes = 1024 * 1024;
    const long MaxArchiveBytes = 64 * 1024 * 1024;
    const long MaxArchiveFileBytes = MaxArchiveBytes + 1024 * 1024;

    internal static int? SlotFromName(string name)
    {
        string stem=Path.GetFileNameWithoutExtension(name);
        if(stem.Length<4||stem[2]!='-'||!int.TryParse(stem.AsSpan(0,2),NumberStyles.None,CultureInfo.InvariantCulture,out int bank)||
           bank is <1 or >64||char.ToUpperInvariant(stem[3]) is not (>= 'A' and <= 'D')||
           stem.Length>4&&stem[4] is not ('_' or '-'))return null;
        return (bank-1)*4+char.ToUpperInvariant(stem[3])-'A';
    }

    internal static IReadOnlyList<ArchivedPreset> Read(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > MaxArchiveFileBytes) throw new InvalidDataException(UiText.T("ZIP archive is too large."));
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        var entries = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name) &&
                Path.GetExtension(entry.Name).Equals(".prst", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (entries.Length == 0) throw new InvalidDataException(UiText.T("ZIP archive contains no GP-200 presets."));
        if (entries.Length > MaxPresets) throw new InvalidDataException(UiText.T("ZIP archive contains too many presets."));

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ArchivedPreset>(entries.Length);
        long totalBytes = 0;
        foreach (var entry in entries)
        {
            if (!names.Add(entry.FullName)) throw new InvalidDataException(UiText.T("ZIP archive contains duplicate preset entries."));
            if (entry.Length < 44 || entry.Length > MaxPresetBytes)
                throw new InvalidDataException(UiText.T("ZIP archive contains an invalid preset size."));
            totalBytes = checked(totalBytes + entry.Length);
            if (totalBytes > MaxArchiveBytes) throw new InvalidDataException(UiText.T("ZIP archive is too large."));

            using var source = entry.Open();
            using var output = new MemoryStream((int)entry.Length);
            byte[] buffer = new byte[81920];
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) != 0)
            {
                if (output.Length + read > MaxPresetBytes)
                    throw new InvalidDataException(UiText.T("ZIP entry expands beyond the preset size limit."));
                output.Write(buffer, 0, read);
            }
            if (output.Length != entry.Length) throw new InvalidDataException(UiText.T("ZIP preset size does not match its directory."));
            byte[] bytes = output.ToArray();
            result.Add(new(entry.FullName, bytes, new PresetDocument(bytes)));
        }
        return result;
    }

    internal static void Write(string path, IEnumerable<(string Name, byte[] Bytes)> presets,bool overwrite=false)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)!;
        string temporary = Path.Combine(directory, $"preset-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                int count = 0;
                long totalBytes = 0;
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (name, bytes) in presets)
                {
                    if (++count > MaxPresets) throw new InvalidDataException(UiText.T("ZIP archive contains too many presets."));
                    if (bytes.Length < 44 || bytes.Length > MaxPresetBytes)
                        throw new InvalidDataException(UiText.T("ZIP archive contains an invalid preset size."));
                    _ = new PresetDocument(bytes);
                    totalBytes = checked(totalBytes + bytes.Length);
                    if (totalBytes > MaxArchiveBytes) throw new InvalidDataException(UiText.T("ZIP archive is too large."));

                    string leafName = string.Concat(Path.GetFileName(name).Select(character =>
                        Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim();
                    if (string.IsNullOrWhiteSpace(leafName)) leafName = "Preset.prst";
                    if (!leafName.EndsWith(".prst", StringComparison.OrdinalIgnoreCase) || !names.Add(leafName))
                        throw new InvalidDataException(UiText.T("ZIP archive contains an invalid or duplicate preset name."));
                    var entry = archive.CreateEntry(leafName, CompressionLevel.Optimal);
                    using var destination = entry.Open();
                    destination.Write(bytes);
                }
                if (count == 0) throw new InvalidDataException(UiText.T("ZIP archive contains no GP-200 presets."));
            }
            File.Move(temporary,fullPath,overwrite);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
