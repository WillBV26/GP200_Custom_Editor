using System.Text.Json;

namespace Gp200.Replica;

internal sealed class PresetCollections
{
    internal sealed class State
    {
        public HashSet<string> Favorites { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string,List<string>> Setlists { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    readonly string path;
    readonly State state;
    readonly object gate=new();

    PresetCollections(string path,State state){this.path=path;this.state=state;}

    internal static PresetCollections Load(string? path=null)
    {
        path??=AppPaths.Collections;
        if(!File.Exists(path))return new(path,new());
        try
        {
            var state=JsonSerializer.Deserialize<State>(File.ReadAllText(path))??throw new InvalidDataException("Preset collections file is empty.");
            state.Favorites=new(state.Favorites??[],StringComparer.OrdinalIgnoreCase);
            state.Setlists=new(state.Setlists??[],StringComparer.OrdinalIgnoreCase);
            foreach(var pair in state.Setlists.ToArray())
            {
                ValidateName(pair.Key);
                if(pair.Value==null||pair.Value.Any(string.IsNullOrWhiteSpace))throw new InvalidDataException("Preset collection contains an invalid entry.");
                state.Setlists[pair.Key]=pair.Value.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            return new(path,state);
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            throw new InvalidDataException($"Could not load local preset collections at {path}: {error.Message}",error);
        }
    }

    internal IReadOnlyList<string> SetlistNames
    {
        get{lock(gate)return state.Setlists.Keys.Order(StringComparer.CurrentCultureIgnoreCase).ToArray();}
    }

    internal bool IsFavorite(LibraryEntry entry)
    {
        lock(gate)return state.Favorites.Contains(Key(entry));
    }

    internal IReadOnlyList<string> Entries(string setlist)
    {
        lock(gate)return state.Setlists.TryGetValue(setlist,out var entries)?entries.ToArray():[];
    }

    internal void ToggleFavorite(LibraryEntry entry)
        => ToggleFavorites([entry]);

    internal void ToggleFavorites(IEnumerable<LibraryEntry> entries)
    {
        lock(gate)
        {
            foreach(string key in entries.Select(Key).Distinct(StringComparer.OrdinalIgnoreCase))
                if(!state.Favorites.Add(key))state.Favorites.Remove(key);
            Save();
        }
    }

    internal void AddToSetlist(string name,LibraryEntry entry)
        => AddManyToSetlist(name,[entry]);

    internal void AddManyToSetlist(string name,IEnumerable<LibraryEntry> presets)
    {
        ValidateName(name);
        lock(gate)
        {
            if(!state.Setlists.TryGetValue(name,out var entries))state.Setlists[name]=entries=[];
            foreach(string key in presets.Select(Key).Distinct(StringComparer.OrdinalIgnoreCase))
                if(!state.Setlists[name].Contains(key,StringComparer.OrdinalIgnoreCase))state.Setlists[name].Add(key);
            Save();
        }
    }

    internal bool RemoveFromSetlist(string name,LibraryEntry entry)
        => RemoveManyFromSetlist(name,[entry]) != 0;

    internal int RemoveManyFromSetlist(string name,IEnumerable<LibraryEntry> entries)
    {
        lock(gate)
        {
            if(!state.Setlists.TryGetValue(name,out var members))return 0;
            var keys=entries.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            int removed=members.RemoveAll(keys.Contains);
            if(removed!=0)Save();
            return removed;
        }
    }

    internal static string Key(LibraryEntry entry)=>entry.DeviceSlot is int slot
        ?$"device:{slot}"
        :"file:"+Path.GetFullPath(entry.Path);

    static void ValidateName(string name)
    {
        if(string.IsNullOrWhiteSpace(name)||name.Length>80||name.Any(char.IsControl))
            throw new ArgumentException("Setlist names must contain 1–80 printable characters.",nameof(name));
        if(name.Equals("All presets",StringComparison.OrdinalIgnoreCase)||name.Equals("Favorites",StringComparison.OrdinalIgnoreCase)||
           name.Equals("Todos los presets",StringComparison.OrdinalIgnoreCase)||name.Equals("Favoritos",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Setlist name is reserved.",nameof(name));
    }

    void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary=Path.Combine(Path.GetDirectoryName(path)!, $"collections-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary,JsonSerializer.Serialize(state,new JsonSerializerOptions{WriteIndented=true}));
            File.Move(temporary,path,true);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
