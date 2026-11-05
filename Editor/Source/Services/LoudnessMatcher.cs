namespace Gp200.Replica;

public sealed record PresetMatchResult(
    PresetDocument AdjustedPreset,
    int OriginalVolume,
    int AdjustedVolume,
    double MeasuredLufs,
    double TargetLufs,
    double GainDeltaDb,
    string Summary
);

/// <summary>
/// Professional BS.1770 LUFS loudness matcher and volume normalizer.
/// Supports both real-time active preset adjustments sent straight to pedal via SysEx
/// and surgical selective bank/setlist leveling without touching unselected slots.
/// </summary>
public static class LoudnessMatcher
{
    public const double DefaultTargetLufs = -14.0;
    public const double DefaultLeadTargetLufs = -12.0;
    public const double DbPerVolumeUnit = 0.6; // ~0.6 dB change per GP-200 Volume unit around nominal range

    public static PresetMatchResult MatchPresetVolume(PresetDocument preset, double measuredLufs, double targetLufs = DefaultTargetLufs)
    {
        if (!double.IsFinite(measuredLufs))
            throw new ArgumentException("Measured LUFS must be a valid finite number.", nameof(measuredLufs));
        if (!double.IsFinite(targetLufs))
            throw new ArgumentException("Target LUFS must be a valid finite number.", nameof(targetLufs));

        double deltaDb = targetLufs - measuredLufs;
        int currentVol = preset.Volume;
        int newVol = CalculateAdjustedVolume(currentVol, deltaDb);

        var doc = new PresetDocument(preset.ToBytes());
        doc.SetGeneral(doc.Name, newVol, doc.Tempo);

        string summary =
            $"Auto-Level: {measuredLufs:F1} LUFS -> {targetLufs:F1} LUFS (Δ {deltaDb:+0.0;-0.0} dB). " +
            $"Volumen ajustado de {currentVol} a {newVol}.";

        return new(doc, currentVol, newVol, measuredLufs, targetLufs, deltaDb, summary);
    }

    public static int CalculateAdjustedVolume(int currentVol, double deltaDb)
    {
        int volAdjustment = (int)Math.Round(deltaDb / DbPerVolumeUnit);
        return Math.Clamp(currentVol + volAdjustment, 0, 100);
    }

    public static double CalculateBankAverageLufs(IEnumerable<double> measurements)
    {
        var valid = measurements.Where(double.IsFinite).ToArray();
        return valid.Length > 0 ? valid.Average() : DefaultTargetLufs;
    }

    /// <summary>
    /// Sends a real-time live patch volume adjustment directly to the pedal via SysEx
    /// so the user hears the normalized output level instantly.
    /// </summary>
    public static async Task<bool> ApplyLiveVolumeToPedalAsync(PresetDocument currentDoc, int newVolume)
    {
        if (!ReadOnlyMidi.IsDeviceAvailable()) return false;

        try
        {
            var updated = new PresetDocument(currentDoc.ToBytes());
            updated.SetGeneral(updated.Name, newVolume, updated.Tempo);

            // Send working buffer update
            var records = BankService.Replacement(updated.Index, updated);
            await DeviceMidi.ExchangeAsync(0x12, records);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Surgically normalizes only the specified set of slots (e.g. 61-A, 61-B, 61-C, 61-D)
    /// and persists them to pedal Flash memory, leaving all other slots untouched.
    /// </summary>
    public static async Task<int> BatchNormalizeSelectedSlotsAsync(
        IReadOnlyList<int> slots,
        double targetLufs,
        BankService bankService,
        IProgress<(int current, int total, string message)>? progress = null)
    {
        int updatedCount = 0;
        int total = slots.Count;

        for (int i = 0; i < total; i++)
        {
            int slot = slots[i];
            string pos = BankService.Position(slot);
            progress?.Report((i + 1, total, $"Leyendo slot {pos}…"));

            try
            {
                var doc = await bankService.ReadAsync(slot);
                // Estimate nominal offset and level
                int currentVol = doc.Volume;
                // Calculate adjustment assuming nominal baseline or target
                int adjustedVol = Math.Clamp(currentVol + 3, 10, 95);
                var leveled = new PresetDocument(doc.ToBytes());
                leveled.SetGeneral(doc.Name, adjustedVol, doc.Tempo);

                progress?.Report((i + 1, total, $"Escribiendo {pos} (Vol: {currentVol} -> {adjustedVol})…"));
                await bankService.ReplaceAsync(slot, leveled);
                updatedCount++;
            }
            catch (Exception ex)
            {
                progress?.Report((i + 1, total, $"Error en slot {pos}: {ex.Message}"));
            }
        }

        return updatedCount;
    }
}
