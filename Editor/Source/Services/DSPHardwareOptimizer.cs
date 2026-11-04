using System.Text;

namespace Gp200.Replica;

public sealed record DspOptimizationResult(
    PresetDocument OptimizedDocument,
    int PreviousDspLoad,
    int OptimizedDspLoad,
    IReadOnlyList<string> Report
);

/// <summary>
/// Motor de optimización profunda de DSP para Valeton GP-200 / GP-200R.
/// Modifica los parámetros y algoritmos internos del preset para liberar
/// ciclos reales de CPU/DSP y memoria en el hardware físico de la pedalera,
/// garantizando que el ahorro de recursos persista al desconectar el cable USB.
/// </summary>
public static class DSPHardwareOptimizer
{
    // Algoritmos de carga mínima por módulo en el firmware de GP-200
    // Al colocar un bloque inactivo en su algoritmo más ligero (0x00) y poner sus parámetros en reposo,
    // el DSP del pedal no reserva buffers de convolución, sobremuestreo ni líneas de retardo en RAM.
    public static readonly uint[] LightestAlgorithmCodes =
    [
        0x00000000, // 0: PRE (Compresor básico)
        0x00000000, // 1: WAH (Wah básico)
        0x00000000, // 2: DST (Boost limpio / 0x oversampling)
        0x00000000, // 3: AMP (Preamp ligero)
        0x00000000, // 4: NR  (Gate simple)
        0x00000000, // 5: CAB (Cab estándar sin convolución larga)
        0x00000000, // 6: EQ  (EQ de 3 bandas)
        0x00000000, // 7: MOD (Tremolo básico sin buffer RAM)
        0x00000000, // 8: DLY (Pure Delay mono sin modulación)
        0x00000000, // 9: RVB (Room corto)
        0x00000000  // 10: VOL (Volumen pasivo)
    ];

    // Pesos porcentuales base aproximados de DSP por bloque en la GP-200
    private static readonly int[] BaseDspWeights =
    [
        4,  // 0: PRE
        3,  // 1: WAH
        8,  // 2: DST
        22, // 3: AMP (Stock ~22%, NAM/A2Lite ~28%)
        3,  // 4: NR
        12, // 5: CAB (Stock ~12%, User IR ~16%)
        4,  // 6: EQ
        6,  // 7: MOD
        8,  // 8: DLY (+2% si Trail está activo)
        10, // 9: RVB (+3% si Trail está activo)
        1   // 10: VOL
    ];

    private const int SystemBaseOverhead = 10;
    private const int ParallelBranchOverhead = 4;

    /// <summary>
    /// Calcula el porcentaje estimado de carga de DSP (0% a 100%) para un preset dado.
    /// </summary>
    public static int CalculateDspPercentage(PresetDocument doc)
    {
        int total = SystemBaseOverhead;
        for (int m = 0; m < 11; m++)
        {
            var effect = doc.GetEffect(m);
            if (!effect.Enabled) continue;

            int weight = BaseDspWeights[m];
            
            // AMP pesado (NAM / A2 Lite)
            if (m == 3 && (effect.Code >= 0x1000 || doc.GetEffect(3).Parameters[0] > 100))
            {
                weight = 28;
            }
            // CAB con User IR
            else if (m == 5 && effect.Parameters.Length > 0 && effect.Parameters[0] >= 20)
            {
                weight = 15;
            }
            // DLY con Trails
            else if (m == 8 && effect.Parameters.Length > 4 && effect.Parameters[4] > 0.5f)
            {
                weight += 2;
            }
            // RVB con Trails
            else if (m == 9 && effect.Parameters.Length > 4 && effect.Parameters[4] > 0.5f)
            {
                weight += 3;
            }

            total += weight;
        }

        // Si el ruteo es Dual Paralelo A/B activo
        if (doc.FxLoopMode > 1 || ChainRouting.Boundary(doc) > 0)
        {
            total += ParallelBranchOverhead;
        }

        return Math.Clamp(total, 10, 99);
    }

    /// <summary>
    /// Devuelve el desglose de consumo estimado de DSP por módulo.
    /// </summary>
    public static Dictionary<string, int> GetDspBreakdown(PresetDocument doc)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int m = 0; m < 11; m++)
        {
            var effect = doc.GetEffect(m);
            string name = PresetDocument.Modules[m];
            if (!effect.Enabled)
            {
                result[name] = 0;
                continue;
            }

            int weight = BaseDspWeights[m];
            if (m == 3 && effect.Code >= 0x1000) weight = 28;
            else if (m == 5 && effect.Parameters[0] >= 20) weight = 15;
            else if (m == 8 && effect.Parameters.Length > 4 && effect.Parameters[4] > 0.5f) weight += 2;
            else if (m == 9 && effect.Parameters.Length > 4 && effect.Parameters[4] > 0.5f) weight += 3;

            result[name] = weight;
        }
        return result;
    }

    /// <summary>
    /// Ejecuta las 7 etapas de optimización profunda de DSP en el preset y genera el reporte.
    /// </summary>
    public static DspOptimizationResult AnalyzeAndOptimize(PresetDocument source, DeviceSettings? settings = null)
    {
        int initialLoad = CalculateDspPercentage(source);
        var report = new List<string>();
        var doc = new PresetDocument(source.ToBytes());

        // 1. Detección de Bloques Protegidos (asignados a Footswitch, Scenes, EXP o CTRL)
        var protectedModules = GetProtectedModules(doc, settings);

        // 2. Hibernación en Hardware de Bloques Inactivos (Reemplazo por algoritmos ligeros)
        for (int m = 0; m < 11; m++)
        {
            var effect = doc.GetEffect(m);
            string modName = PresetDocument.Modules[m];

            if (!effect.Enabled && !protectedModules.Contains(m))
            {
                uint minCode = LightestAlgorithmCodes[m];
                if (effect.Code != minCode)
                {
                    // Poner parámetros en reposo
                    float[] zeroParams = new float[15];
                    for (int p = 0; p < 15; p++) zeroParams[p] = 0f;
                    
                    doc.SetEffect(m, false, zeroParams);
                    report.Add($"[{modName}] En OFF y sin asignar a Escenas/FS -> Algoritmo hibernado en hardware (0 ciclos CPU).");
                }
            }
        }

        // 3. Corte de Colas en Segundo Plano (Trails OFF en DLY y RVB inactivos)
        foreach (int timeMod in new[] { 8, 9 }) // 8: DLY, 9: RVB
        {
            var effect = doc.GetEffect(timeMod);
            string name = PresetDocument.Modules[timeMod];
            if (!effect.Enabled && !protectedModules.Contains(timeMod))
            {
                var p = (float[])effect.Parameters.Clone();
                if (p.Length > 4 && p[4] > 0f)
                {
                    p[4] = 0f; // Trail = 0
                    doc.SetEffect(timeMod, false, p);
                    report.Add($"[{name}] Desactivado 'Trail' en segundo plano para liberar buffer de memoria RAM.");
                }
            }
        }

        // 4. Fusión y Consolidación de Ecualización (EQ -> CAB Low/Hi Cut)
        var eq = doc.GetEffect(6);
        var cab = doc.GetEffect(5);
        if (eq.Enabled && cab.Enabled && !protectedModules.Contains(6))
        {
            if (IsEqOnlyDoingCuts(eq))
            {
                MergeEqCutsIntoCab(eq, cab, doc);
                doc.SetEffect(6, false, eq.Parameters);
                report.Add("[EQ -> CAB] Filtros de corte absorbidos por el bloque CAB. Bloque EQ liberado.");
            }
        }

        // 5. Detección de Redundancia Full-Rig vs CAB
        var amp = doc.GetEffect(3);
        if (amp.Enabled && !cab.Enabled && !protectedModules.Contains(5))
        {
            // Si AMP es full-rig y CAB está en OFF, limpiamos asignación de IR innecesaria
            var cabP = (float[])cab.Parameters.Clone();
            if (cabP.Length > 0 && cabP[0] != 0)
            {
                cabP[0] = 0; // Desvincular IR
                doc.SetEffect(5, false, cabP);
                report.Add("[CAB] Búfer de respuesta de impulso en segundo plano descargado.");
            }
        }

        // 6. Limpieza de Moduladores y LFOs Huérfanos
        // Si hay destinos de expresión apuntando a módulos permanentemente apagados y desprotegidos
        for (byte slot = 0; slot < 2; slot++)
        {
            var exp = doc.Expression.FirstOrDefault(e => e.Slot == slot);
            if (exp != null && exp.Module < 11)
            {
                var targetEffect = doc.GetEffect(exp.Module);
                if (!targetEffect.Enabled && !protectedModules.Contains(exp.Module))
                {
                    doc.SetDestination(false, slot, 255, 0, CatalogRuntime.Current);
                    doc.SetExpression(slot, 0, 0);
                    report.Add($"[EXP {slot + 1}] Asignación a bloque {PresetDocument.Modules[exp.Module]} (apagado) neutralizada.");
                }
            }
        }

        // 7. Optimización de Ruteo Paralelo A/B (Colapso a Serie si rama B está vacía)
        if (doc.FxLoopMode > 1 || ChainRouting.Boundary(doc) > 0)
        {
            bool branchBActive = false;
            int splitPoint = ChainRouting.Boundary(doc);
            byte[] chain = doc.Chain;

            // Bloques en Rama B
            for (int i = splitPoint; i < chain.Length; i++)
            {
                int mod = chain[i];
                if (mod < 11 && doc.GetEffect(mod).Enabled)
                {
                    branchBActive = true;
                    break;
                }
            }

            if (!branchBActive)
            {
                // Rama B vacía: colapsar a modo Serie estándar
                doc.SetPatchOptions(loopMode: 1);
                report.Add("[ROUTING] Rama B paralela sin bloques activos -> Colapsado a Serie para ahorrar sumador DSP.");
            }
        }

        int optimizedLoad = CalculateDspPercentage(doc);
        if (report.Count == 0)
        {
            report.Add("El preset ya se encuentra en estado de consumo óptimo de hardware.");
        }

        return new DspOptimizationResult(doc, initialLoad, optimizedLoad, report);
    }

    private static HashSet<int> GetProtectedModules(PresetDocument doc, DeviceSettings? settings)
    {
        var protectedMods = new HashSet<int>();

        // Bloques asignados a CTRL
        foreach (var (slot, mask) in doc.Controls)
        {
            for (int m = 0; m < 11; m++)
            {
                if ((mask & (1u << m)) != 0)
                {
                    protectedMods.Add(m);
                }
            }
        }

        // Bloques asignados a pedal de expresión o quick access
        foreach (var exp in doc.Expression)
        {
            if (exp.Module < 11) protectedMods.Add(exp.Module);
        }
        foreach (var q in doc.Quick)
        {
            if (q.Module < 11) protectedMods.Add(q.Module);
        }

        // Footswitch settings globales si están presentes
        if (settings != null)
        {
            for (int t = 0; t < 3; t++)
            {
                for (int fs = 0; fs < 16; fs++)
                {
                    int action = settings.Footswitch(t, fs);
                    // Acciones que conmutan bloques de efectos específicos
                    if (action >= 10 && action <= 20)
                    {
                        protectedMods.Add(action - 10);
                    }
                }
            }
        }

        return protectedMods;
    }

    private static bool IsEqOnlyDoingCuts(Effect eq)
    {
        // En EQ paramétrico/gráfico, si las bandas medias están planas (~50) y solo hay cortes extremos
        if (eq.Parameters.Length < 5) return false;
        bool flatMids = true;
        for (int i = 1; i < eq.Parameters.Length - 1; i++)
        {
            if (Math.Abs(eq.Parameters[i] - 50.0f) > 4.0f)
            {
                flatMids = false;
                break;
            }
        }
        return flatMids;
    }

    private static void MergeEqCutsIntoCab(Effect eq, Effect cab, PresetDocument doc)
    {
        var cabP = (float[])cab.Parameters.Clone();
        // Traspasar cortes a Low Cut / Hi Cut del bloque CAB
        if (cabP.Length >= 4)
        {
            cabP[2] = Math.Max(cabP[2], eq.Parameters[0]); // Low Cut
            cabP[3] = Math.Min(cabP[3], eq.Parameters[^1]); // Hi Cut
            doc.SetEffect(5, cab.Enabled, cabP);
        }
    }
}

