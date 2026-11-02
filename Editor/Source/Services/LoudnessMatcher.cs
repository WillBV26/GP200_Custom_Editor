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

public static class LoudnessMatcher
{
    public const double DefaultTargetLufs = -14.0;
    const double DbPerVolumeUnit = 0.6; // Approximate dB change per GP-200 Volume unit around nominal range

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
}

