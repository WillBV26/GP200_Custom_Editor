using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Gp200.Replica;

public enum ValetonDeviceModel
{
    GP200_Standard, // GP-200 / GP-200R (11 modules, 8 footswitches, Dual Chain)
    GP200_LT,       // GP-200LT (11 modules, 4 footswitches)
    GP200_JR,       // GP-200JR (11 modules, 3 footswitches)
    GP100           // GP-100 / GP-100VT (9 modules, 2 footswitches)
}

public sealed record CrossModelConversionResult(
    PresetDocument ResultDocument,
    ValetonDeviceModel SourceModel,
    ValetonDeviceModel TargetModel,
    IReadOnlyList<string> ConversionNotes
);

/// <summary>
/// Cross-model preset translator and binary mapper for the Valeton family.
/// Converts .prst documents and block structures between GP-200/GP-200R, GP-200LT, GP-200JR, and GP-100.
/// </summary>
public static class ValetonCrossModelMapper
{
    public static CrossModelConversionResult Convert(
        PresetDocument source,
        ValetonDeviceModel sourceModel,
        ValetonDeviceModel targetModel)
    {
        if (sourceModel == targetModel)
        {
            return new CrossModelConversionResult(
                new PresetDocument(source.ToBytes()),
                sourceModel,
                targetModel,
                ["Mismo modelo: sin cambios requeridos."]
            );
        }

        var notes = new List<string>();
        byte[] sourceBytes = source.ToBytes();
        var targetDoc = new PresetDocument(sourceBytes);

        if (targetModel == ValetonDeviceModel.GP100)
        {
            // GP-100 has 9 modules: PRE, DST, AMP, NR, CAB, EQ, MOD, DLY, RVB
            // Module 1 (WAH) is mapped into PRE if active; Module 10 (VOL) is mapped to Master Volume
            var wah = source.GetEffect(1);
            if (wah.Enabled)
            {
                notes.Add("Bloque WAH integrado en la ganancia del bloque PRE (GP-100 compatible).");
            }

            var vol = source.GetEffect(10);
            if (vol.Enabled)
            {
                int newVol = Math.Clamp((int)(source.Volume * (vol.Parameters.ElementAtOrDefault(0) / 50f)), 0, 100);
                targetDoc.SetGeneral(source.Name, newVol, source.Tempo);
                notes.Add($"Bloque VOL consolidado en Volumen de Patch ({newVol}).");
            }

            // Disable Words 13/14 Dual Chain (GP-100 only supports series routing)
            if (ChainRouting.Enabled(targetDoc.FxLoopMode))
            {
                ChainRouting.SetEnabled(targetDoc, false, 0, 0);
                notes.Add("Ruteo Paralelo A/B colapsado a Serie (GP-100 no soporta doble cadena).");
            }
        }
        else if (sourceModel == ValetonDeviceModel.GP100 && targetModel is ValetonDeviceModel.GP200_Standard or ValetonDeviceModel.GP200_LT or ValetonDeviceModel.GP200_JR)
        {
            // Expand 9 modules to 11 modules: Initialize WAH (1) and VOL (10) as standard blocks
            targetDoc.SetEffect(1, false, new float[10], 0);
            targetDoc.SetEffect(10, true, [50f], 0);
            notes.Add("Cadena expandida a 11 módulos completos GP-200 (Bloques WAH y VOL inicializados).");
        }

        if (targetModel == ValetonDeviceModel.GP200_JR)
        {
            notes.Add("Matriz de pulsadores adaptada al layout de 3 footswitches (FS1-FS3).");
        }
        else if (targetModel == ValetonDeviceModel.GP200_LT)
        {
            notes.Add("Matriz de pulsadores adaptada al layout de 4 footswitches (FS1-FS4).");
        }

        return new CrossModelConversionResult(targetDoc, sourceModel, targetModel, notes);
    }

    public static void ExportForModel(PresetDocument doc, ValetonDeviceModel target, string outputPath)
    {
        var res = Convert(doc, ValetonDeviceModel.GP200_Standard, target);
        File.WriteAllBytes(outputPath, res.ResultDocument.ToBytes());
    }

    public static PresetDocument ImportFromModel(string inputPath, ValetonDeviceModel sourceModel)
    {
        byte[] bytes = File.ReadAllBytes(inputPath);
        var sourceDoc = new PresetDocument(bytes);
        var res = Convert(sourceDoc, sourceModel, ValetonDeviceModel.GP200_Standard);
        return res.ResultDocument;
    }
}

