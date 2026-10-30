using System.Text.Json;
using System.Text.Json.Serialization;

namespace NexusClick.Engine;

public sealed record MacroDocument(int Version, Guid Id, string Name, MacroPlan Plan)
{
    public void Validate()
    {
        if (Version != 1) throw new InvalidDataException("Unsupported macro format version.");
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || Name.Any(char.IsControl)) throw new InvalidDataException("Invalid macro identity.");
        if (Plan is null) throw new InvalidDataException("Macro plan is required.");
        Plan.Validate();
    }
}
/// <summary>Reads and writes bounded macro definitions; import never executes them.</summary>
public sealed class MacroStore
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private static readonly JsonSerializerOptions Options = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true, MaxDepth = 32, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
    public static MacroDocument Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length > ProfileStore.MaximumBytes) throw new InvalidDataException("Macro file is too large.");
        try
        {
            var document = JsonSerializer.Deserialize<MacroDocument>(data, Options) ?? throw new InvalidDataException("Empty macro document.");
            document.Validate(); return document;
        }
        catch (JsonException ex) { throw new InvalidDataException("Malformed macro JSON.", ex); }
        catch (NotSupportedException ex) { throw new InvalidDataException("Unsupported macro data.", ex); }
    }
    public async Task<MacroDocument> LoadAsync(string path, CancellationToken token = default)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (file.Length > ProfileStore.MaximumBytes) throw new InvalidDataException("Macro file is too large.");
        using var buffer = new MemoryStream(); var chunk = new byte[8192];
        while (true)
        {
            var count = await file.ReadAsync(chunk, token).ConfigureAwait(false);
            if (count == 0) break;
            if (buffer.Length + count > ProfileStore.MaximumBytes) throw new InvalidDataException("Macro file is too large.");
            buffer.Write(chunk, 0, count);
        }
        return Parse(buffer.ToArray());
    }
    public async Task SaveAsync(string path, MacroDocument document, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(document); document.Validate();
        var data = JsonSerializer.SerializeToUtf8Bytes(document, Options);
        if (data.Length > ProfileStore.MaximumBytes) throw new InvalidDataException("Macro file is too large.");
        _ = Parse(data);
        var destination = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(destination) ?? throw new ArgumentException("A directory is required.", nameof(path));
        await _writeGate.WaitAsync(token).ConfigureAwait(false);
        var temp = Path.Combine(directory, ".nexus-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(directory);
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await file.WriteAsync(data, token).ConfigureAwait(false);
                await file.FlushAsync(token).ConfigureAwait(false);
                file.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested(); File.Move(temp, destination, overwrite: true);
        }
        finally { try { File.Delete(temp); } finally { _writeGate.Release(); } }
    }
}
