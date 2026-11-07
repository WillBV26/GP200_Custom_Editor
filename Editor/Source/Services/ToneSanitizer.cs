using System;
using System.Collections.Generic;
using System.Linq;

namespace Gp200.Replica;

/// <summary>
/// Acoustic and musical sound engineering laws for Valeton GP-200 AI presets.
/// Automatically enforces professional gain staging, cabinet filtering,
/// standard series chain order, and time-based mix moderation.
/// </summary>
public static class ToneSanitizer
{
    // Canonical studio series chain order for GP-200:
    // WAH(1) -> PRE(0) -> DST(2) -> AMP(3) -> NR(4) -> CAB(5) -> EQ(6) -> MOD(7) -> DLY(8) -> RVB(9) -> VOL(10)
    public static readonly byte[] CanonicalChain = [1, 0, 2, 3, 4, 5, 6, 7, 8, 9, 10];

    /// <summary>
    /// Validates and acoustically cleans up a PresetDocument to guarantee musicality,
    /// avoiding phase issues, shrill highs, mud, unmanaged high-gain hiss, or chaotic order.
    /// </summary>
    public static PresetDocument ValidateAndFixMusicality(PresetDocument document, Catalog? catalog = null)
    {
        if (document == null) return document!;
        var cat = catalog ?? CatalogRuntime.Current;

        // 1. Sanitize Chain Order
        SanitizeChainOrder(document);

        // 2. Sanitize Master Volume & Output
        SanitizeVolumeAndTempo(document);

        // 3. Sanitize AMP & CAB Pairing and Low/High Cuts
        SanitizeAmpAndCab(document, cat);

        // 4. Sanitize Gain Staging & Noise Gate (NR)
        SanitizeGainAndGate(document, cat);

        // 5. Sanitize Delay & Reverb Mix Levels
        SanitizeTimeBasedEffects(document, cat);

        return document;
    }

    static void SanitizeChainOrder(PresetDocument doc)
    {
        var chain = doc.Chain.ToList();

        // Must be a valid 11-module permutation containing exactly 0 through 10
        bool isValidPermutation = chain.Count == 11 &&
                                  chain.Distinct().Count() == 11 &&
                                  chain.All(x => x <= 10);

        if (!isValidPermutation)
        {
            doc.SetChain(CanonicalChain);
            return;
        }

        bool modified = false;

        // Physical module indices:
        // 0: PRE, 1: WAH, 2: DST, 3: AMP, 4: NR, 5: CAB, 6: EQ, 7: MOD, 8: DLY, 9: RVB, 10: VOL
        int ampIdx = chain.IndexOf(3);
        int cabIdx = chain.IndexOf(5);

        // LAW 1: CAB must NEVER be placed before AMP in guitar signal flow
        if (cabIdx >= 0 && ampIdx >= 0 && cabIdx < ampIdx)
        {
            chain.Remove(5);
            // Insert CAB right after NR (4) if present, or right after AMP (3)
            int nrIdx = chain.IndexOf(4);
            int insertPos = nrIdx >= 0 && nrIdx > ampIdx ? nrIdx + 1 : chain.IndexOf(3) + 1;
            chain.Insert(Math.Clamp(insertPos, 0, chain.Count), 5);
            modified = true;
        }

        // LAW 2: Distortion (2) must NEVER be placed after Reverb (9) or Delay (8)
        int dstIdx = chain.IndexOf(2);
        int dlyIdx = chain.IndexOf(8);
        int rvbIdx = chain.IndexOf(9);

        if (dstIdx >= 0 && ((dlyIdx >= 0 && dstIdx > dlyIdx) || (rvbIdx >= 0 && dstIdx > rvbIdx)))
        {
            chain.Remove(2);
            int targetAmpIdx = chain.IndexOf(3);
            int insertPos = targetAmpIdx >= 0 ? targetAmpIdx : 0;
            chain.Insert(Math.Clamp(insertPos, 0, chain.Count), 2);
            modified = true;
        }

        if (modified)
        {
            doc.SetChain(chain);
        }
    }

    static void SanitizeVolumeAndTempo(PresetDocument doc)
    {
        int vol = doc.Volume;
        int tempo = doc.Tempo;
        bool needsUpdate = false;

        // Tame dangerous output volumes that cause digital clipping or deafening jumps
        if (vol > 85)
        {
            vol = 80;
            needsUpdate = true;
        }
        else if (vol < 30)
        {
            vol = 60;
            needsUpdate = true;
        }

        // Keep tempo in valid pedal bounds (40-250 BPM)
        if (tempo is < 40 or > 250)
        {
            tempo = Math.Clamp(tempo, 40, 250);
            needsUpdate = true;
        }

        if (needsUpdate)
        {
            doc.SetGeneral(doc.Name, vol, tempo);
        }
    }

    static void SanitizeAmpAndCab(PresetDocument doc, Catalog? cat)
    {
        var ampEf = doc.GetEffect(3);
        var cabEf = doc.GetEffect(5);

        // If AMP is active, CAB should be active unless it's a full-rig NAM
        bool isFullRigNam = false;
        if (cat != null && cat.IsMx42)
        {
            var ampIdentity = cat.Identity(3, ampEf.Code);
            if (ampIdentity.EffectiveFamily == 11 && ampIdentity.Ordinal < cat.Mx42NamLabels.Length)
            {
                string label = cat.Mx42NamLabels[ampIdentity.Ordinal];
                if (label.Contains("Full", StringComparison.OrdinalIgnoreCase) ||
                    label.Contains("Rig", StringComparison.OrdinalIgnoreCase))
                {
                    isFullRigNam = true;
                }
            }
        }

        if (ampEf.Enabled && !cabEf.Enabled && !isFullRigNam)
        {
            // Auto-enable paired Cabinet
            doc.SetEffect(5, true, cabEf.Parameters);
            cabEf = doc.GetEffect(5);
        }

        // Whenever CAB is active, enforce anti-fizz high cut and anti-mud low cut
        if (cabEf.Enabled && cat != null)
        {
            var cabAlgo = cat.Find(5, cabEf.Code);
            if (cabAlgo != null && cabAlgo.Parameters.Count > 0)
            {
                var cloneParams = (float[])cabEf.Parameters.Clone();
                bool cabModified = false;

                foreach (var param in cabAlgo.Parameters)
                {
                    string pName = param.Name.ToLowerInvariant();
                    var (min, max) = param.Bounds(cabEf);

                    // Low Cut (HPF) Enforcement: 80 Hz - 110 Hz (default 85 Hz)
                    if (pName.Contains("low cut") || pName.Contains("lowcut") || pName.Contains("hpf"))
                    {
                        float currentVal = cloneParams.Length > param.Slot ? cloneParams[param.Slot] : 20f;
                        if (currentVal < 70f || currentVal > 150f)
                        {
                            cloneParams[param.Slot] = Math.Clamp(85f, min, max);
                            cabModified = true;
                        }
                    }
                    // High Cut (LPF) Enforcement: 7500 Hz - 9500 Hz (default 8500 Hz)
                    else if (pName.Contains("high cut") || pName.Contains("highcut") || pName.Contains("lpf"))
                    {
                        float currentVal = cloneParams.Length > param.Slot ? cloneParams[param.Slot] : 20000f;
                        if (currentVal > 10500f || currentVal < 5000f)
                        {
                            cloneParams[param.Slot] = Math.Clamp(8500f, min, max);
                            cabModified = true;
                        }
                    }
                }

                if (cabModified)
                {
                    doc.SetEffect(5, cabEf.Enabled, cloneParams);
                }
            }
        }
    }

    static void SanitizeGainAndGate(PresetDocument doc, Catalog? cat)
    {
        var ampEf = doc.GetEffect(3);
        var dstEf = doc.GetEffect(2);
        var nrEf = doc.GetEffect(4);

        float ampGain = 50f;
        string ampName = "";
        if (cat != null)
        {
            var ampAlgo = cat.Find(3, ampEf.Code);
            if (ampAlgo != null)
            {
                ampName = ampAlgo.Name.ToLowerInvariant();
                var gainParam = ampAlgo.Parameters.FirstOrDefault(p =>
                    p.Name.Contains("gain", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Contains("drive", StringComparison.OrdinalIgnoreCase));
                int slot = gainParam?.Slot ?? 0;
                if (ampEf.Parameters.Length > slot) ampGain = ampEf.Parameters[slot];
            }
        }

        float dstGain = 0f;
        if (dstEf.Enabled && cat != null)
        {
            var dstAlgo = cat.Find(2, dstEf.Code);
            if (dstAlgo != null)
            {
                var gainParam = dstAlgo.Parameters.FirstOrDefault(p =>
                    p.Name.Contains("gain", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Contains("drive", StringComparison.OrdinalIgnoreCase));
                int slot = gainParam?.Slot ?? 0;
                if (dstEf.Parameters.Length > slot) dstGain = dstEf.Parameters[slot];

                // If both Amp and Overdrive are cranked to 85+, tame DST Gain to boost level
                if (ampGain > 75f && dstGain > 75f)
                {
                    var dstParams = (float[])dstEf.Parameters.Clone();
                    dstParams[slot] = 30f; // clean TS9 boost mode
                    var volParam = dstAlgo.Parameters.FirstOrDefault(p =>
                        p.Name.Contains("vol", StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Contains("level", StringComparison.OrdinalIgnoreCase));
                    if (volParam != null && dstParams.Length > volParam.Slot)
                    {
                        var (min, max) = volParam.Bounds(dstEf);
                        dstParams[volParam.Slot] = Math.Clamp(65f, min, max);
                    }
                    doc.SetEffect(2, dstEf.Enabled, dstParams);
                }
            }
        }

        // High gain detection
        bool isHighGain = ampGain > 55f ||
                          ampName.Contains("5150") ||
                          ampName.Contains("recto") ||
                          ampName.Contains("fire") ||
                          ampName.Contains("slo") ||
                          ampName.Contains("lead") ||
                          ampName.Contains("metal") ||
                          ampName.Contains("high") ||
                          ampName.Contains("uber") ||
                          ampName.Contains("diesel") ||
                          (ampEf.Enabled && dstEf.Enabled && dstGain > 40f);

        // If High Gain, ensure Noise Gate (NR) is active and threshold is calibrated
        if (isHighGain)
        {
            var nrParams = (float[])nrEf.Parameters.Clone();
            bool nrModified = false;

            if (!nrEf.Enabled)
            {
                nrModified = true;
            }

            // Calibrate threshold parameter (typically slot 0 in GP-200 NR)
            if (nrParams.Length > 0 && nrParams[0] < 30f)
            {
                nrParams[0] = 38f;
                nrModified = true;
            }

            if (nrModified)
            {
                doc.SetEffect(4, true, nrParams);
            }
        }
    }

    static void SanitizeTimeBasedEffects(PresetDocument doc, Catalog? cat)
    {
        bool isAmbientOrWorship = (doc.Name ?? "").Contains("worship", StringComparison.OrdinalIgnoreCase) ||
                                 (doc.Name ?? "").Contains("ambient", StringComparison.OrdinalIgnoreCase) ||
                                 (doc.Notes ?? "").Contains("worship", StringComparison.OrdinalIgnoreCase) ||
                                 (doc.Notes ?? "").Contains("ambient", StringComparison.OrdinalIgnoreCase);

        // Sanitize Delay (Module 8)
        var dlyEf = doc.GetEffect(8);
        if (dlyEf.Enabled && cat != null)
        {
            var dlyAlgo = cat.Find(8, dlyEf.Code);
            if (dlyAlgo != null)
            {
                var mixParam = dlyAlgo.Parameters.FirstOrDefault(p =>
                    p.Name.Equals("mix", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Contains("e.level", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Contains("dly mix", StringComparison.OrdinalIgnoreCase));

                if (mixParam != null && dlyEf.Parameters.Length > mixParam.Slot)
                {
                    float currentMix = dlyEf.Parameters[mixParam.Slot];
                    float maxAllowed = isAmbientOrWorship ? 40f : 32f;
                    if (currentMix > maxAllowed)
                    {
                        var dlyParams = (float[])dlyEf.Parameters.Clone();
                        var (min, max) = mixParam.Bounds(dlyEf);
                        dlyParams[mixParam.Slot] = Math.Clamp(maxAllowed, min, max);
                        doc.SetEffect(8, dlyEf.Enabled, dlyParams);
                    }
                }
            }
        }

        // Sanitize Reverb (Module 9)
        var rvbEf = doc.GetEffect(9);
        if (rvbEf.Enabled && cat != null)
        {
            var rvbAlgo = cat.Find(9, rvbEf.Code);
            if (rvbAlgo != null)
            {
                var mixParam = rvbAlgo.Parameters.FirstOrDefault(p =>
                    p.Name.Equals("mix", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Contains("e.level", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Contains("rvb mix", StringComparison.OrdinalIgnoreCase));

                if (mixParam != null && rvbEf.Parameters.Length > mixParam.Slot)
                {
                    float currentMix = rvbEf.Parameters[mixParam.Slot];
                    float maxAllowed = isAmbientOrWorship ? 45f : 35f;
                    if (currentMix > maxAllowed)
                    {
                        var rvbParams = (float[])rvbEf.Parameters.Clone();
                        var (min, max) = mixParam.Bounds(rvbEf);
                        rvbParams[mixParam.Slot] = Math.Clamp(maxAllowed, min, max);
                        doc.SetEffect(9, rvbEf.Enabled, rvbParams);
                    }
                }
            }
        }
    }
}

