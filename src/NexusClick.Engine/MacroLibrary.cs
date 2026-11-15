using System.Text.Json;
namespace NexusClick.Engine;

public sealed record MacroGroup(Guid Id, string Name);
public sealed record MacroLibraryItem(Guid GroupId, MacroDocument Macro);
public sealed record MacroLibraryData(int Version, List<MacroGroup> Groups, List<MacroLibraryItem> Items);

/// <summary>Local macro library. Names are data, never filesystem paths.</summary>
public sealed class MacroLibrary
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented=true, MaxDepth=32 };
    public MacroLibrary(string path) => _path=Path.GetFullPath(path);
    public MacroLibraryData Load()
    {
        if (!File.Exists(_path)) return new(1,new(),new());
        if (new FileInfo(_path).Length>5_000_000) throw new InvalidDataException("Macro library is too large.");
        MacroLibraryData data;
        try { data=JsonSerializer.Deserialize<MacroLibraryData>(File.ReadAllText(_path),Options) ?? throw new InvalidDataException("Empty macro library."); }
        catch(JsonException ex) { throw new InvalidDataException("Invalid macro library.",ex); }
        Validate(data); return data;
    }
    public void Save(MacroLibraryData data)
    {
        Validate(data);
        var bytes=JsonSerializer.SerializeToUtf8Bytes(data,Options);
        if(bytes.Length>5_000_000) throw new InvalidDataException("Macro library is too large.");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp=_path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { stream.Write(bytes); stream.Flush(true); } File.Move(temp,_path,true); }
        finally { if(File.Exists(temp)) File.Delete(temp); }
    }
    private static void Validate(MacroLibraryData data)
    {
        if(data.Version!=1 || data.Groups is null || data.Items is null || data.Groups.Count>100 || data.Items.Count>1000) throw new InvalidDataException("Invalid macro library.");
        var ids=new HashSet<Guid>();
        foreach(var g in data.Groups) if(g is null || g.Id==Guid.Empty || !ids.Add(g.Id) || !ValidName(g.Name)) throw new InvalidDataException("Invalid macro group.");
        var items=new HashSet<Guid>();
        foreach(var item in data.Items) { if(item is null || !ids.Contains(item.GroupId) || item.Macro is null || !items.Add(item.Macro.Id)) throw new InvalidDataException("Invalid macro item."); item.Macro.Validate(); }
    }
    public static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Length<=80 && !name.Any(char.IsControl);
}
