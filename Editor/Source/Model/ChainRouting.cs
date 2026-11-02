namespace Gp200.Replica;

internal static class ChainRouting
{
    internal static bool ValidMode(int mode)=>mode is 0 or 1||mode is >=0x80 and <=0x8b||mode is >=0x90 and <=0x9b;
    internal static bool Enabled(int mode)=>mode is >=0x80 and <=0x8b;
    internal static bool Extended(int mode)=>ValidMode(mode)&&mode>=0x80;
    internal static void SetEnabled(PresetDocument p,bool enabled,int split,int nativeMode,int? start=null,int? end=null)
    {
        var c=Mx42ChainConfig.GetOrDefault(p);
        int s = start ?? Math.Clamp(c.Start <= split ? c.Start : p.RoutingRaw[0] <= split ? p.RoutingRaw[0] : 0, 0, split);
        int r = end ?? Math.Clamp(c.End >= split ? c.End : p.RoutingRaw[1] >= split ? p.RoutingRaw[1] : 11, split, 11);
        if(enabled && start == null && end == null)
        {
            if (s == r || s == 0 || r == 11 || s >= split || r <= split)
            {
                s = Math.Min(4, split);
                r = Math.Min(11, Math.Max(split + 2, 8));
            }
        }
        c = c with { Start = s, Split = split, End = r };
        p.SetCustomRouting(enabled ? 0x80 | split : nativeMode, enabled ? s : p.RoutingRaw[0], enabled ? split : p.RoutingRaw[0], enabled ? r : p.RoutingRaw[1]);
        c.Write(p);
    }
    internal static int Boundary(PresetDocument p)=>Mx42ChainConfig.GetOrDefault(p).Split;
    internal static bool Independent(PresetDocument p)=>Mx42ChainUsb.HasSavedConfig(p);
    internal static float Blend(PresetDocument p)=>Mx42ChainConfig.GetOrDefault(p).Blend;
    internal static (int frames,bool inverted) Alignment(PresetDocument p){var c=Mx42ChainConfig.GetOrDefault(p);return(c.Align,c.Inverted);}
    internal static void SetBlend(PresetDocument p,float value){if(!Enabled(p.FxLoopMode)||!float.IsFinite(value)||value is <0 or >100)throw new ArgumentOutOfRangeException(nameof(value));(Mx42ChainConfig.GetOrDefault(p) with{Blend=value}).Write(p);}
    internal static void SetAlignment(PresetDocument p,int frames,bool inverted){if(!Enabled(p.FxLoopMode)||frames is <-32 or >32)throw new ArgumentOutOfRangeException(nameof(frames));(Mx42ChainConfig.GetOrDefault(p) with{Align=frames,Inverted=inverted}).Write(p);}
    internal static void MoveBlock(PresetDocument p,byte module,int destination)
    {
        var order=p.Chain.ToList();int from=order.IndexOf(module);if(from<0)return;
        int s=p.RoutingRaw[0],b=Boundary(p),r=p.RoutingRaw[1];int Group(int i)=>i<s?0:i<b?1:i<r?2:3;
        int[] counts=[s,b-s,r-b,11-r];counts[Group(from)]--;counts[Group(Math.Clamp(destination,0,11))]++;
        order.RemoveAt(from);order.Insert(Math.Clamp(destination-(from<destination?1:0),0,order.Count),module);p.SetChain(order);
        int newS = counts[0], newB = counts[0] + counts[1], newR = counts[0] + counts[1] + counts[2];
        p.SetCustomRouting(p.FxLoopMode,newS,newB,newR);
        if(CatalogRuntime.Current.IsMx42)
        {
            var c = Mx42ChainConfig.GetOrDefault(p);
            (c with { Start = newS, Split = newB, End = newR }).Write(p);
        }
    }
    internal static byte[][] Groups(PresetDocument p)
    {
        int s=p.RoutingRaw[0],b=Boundary(p),r=p.RoutingRaw[1];
        if(CatalogRuntime.Current.IsMx42){var c=Mx42ChainConfig.GetOrDefault(p);s=c.Start;b=c.Split;r=c.End;}
        return [p.Chain[..s],p.Chain[s..b],p.Chain[b..r],p.Chain[r..]];
    }
    // The target rank is measured AFTER removing the dragged tile, as in the VST.
    // An explicit group makes empty branches and coincident S/P/R unambiguous.
    internal static void MoveToGroup(PresetDocument p,byte module,int group,int rank)
    {
        if(!Enabled(p.FxLoopMode)||group is <0 or >3)throw new ArgumentOutOfRangeException(nameof(group));
        var groups=Groups(p).Select(x=>x.ToList()).ToArray();
        if(!groups.Any(x=>x.Remove(module)))throw new ArgumentOutOfRangeException(nameof(module));
        groups[group].Insert(Math.Clamp(rank,0,groups[group].Count),module);
        p.SetChain(groups.SelectMany(x=>x));
        int s=groups[0].Count,b=s+groups[1].Count,r=b+groups[2].Count;
        p.SetCustomRouting(p.FxLoopMode,s,b,r);
        if(CatalogRuntime.Current.IsMx42)
        {
            var c = Mx42ChainConfig.GetOrDefault(p);
            (c with { Start = s, Split = b, End = r }).Write(p);
        }
    }
    internal static WireRecord Mode(int mode){if(!ValidMode(mode))throw new InvalidDataException("Unknown CHAIN routing mode.");return new(6,[5,0,(byte)mode,0]);}
}
