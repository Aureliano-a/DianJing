using System.Text.Json;
using System.Text.Json.Serialization;

namespace NexusClick.Engine;

public sealed record Profile(Guid Id, string Name, InputPlan Plan, bool Favorite = false, string Color = "#32DFF2")
{
    public void Validate()
    {
        if (Id == Guid.Empty) throw new InvalidDataException("Profile ID is required.");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || Name.Any(char.IsControl)) throw new InvalidDataException("Invalid profile name.");
        if (Color is null || Color.Length != 7 || Color[0] != '#' || !Color.Skip(1).All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid profile color.");
        if (Plan is null) throw new InvalidDataException("Input plan is required.");
        try { Plan.Validate(); } catch (ArgumentException ex) { throw new InvalidDataException("Invalid input plan.", ex); }
    }
}
public sealed record ProfileDocument(int Version, IReadOnlyList<Profile> Profiles)
{
    public const int CurrentVersion = 1;
    public void Validate()
    {
        if (Version != CurrentVersion) throw new InvalidDataException("Unsupported profile format version.");
        if (Profiles is null || Profiles.Count > 1000) throw new InvalidDataException("Invalid number of profiles.");
        var ids = new HashSet<Guid>();
        foreach (var profile in Profiles)
        {
            if (profile is null) throw new InvalidDataException("Null profile.");
            profile.Validate();
            if (!ids.Add(profile.Id)) throw new InvalidDataException("Duplicate profile ID.");
        }
    }
}

/// <summary>Bounded, validated local JSON storage. Import only parses; it never executes profiles.</summary>
public sealed class ProfileStore
{
    public const int MaximumBytes = 1_048_576;
    private static readonly JsonSerializerOptions Options = CreateOptions();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private static JsonSerializerOptions CreateOptions()
    {
        var o = new JsonSerializerOptions { WriteIndented = true, MaxDepth = 32, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        o.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return o;
    }
    public static ProfileDocument Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaximumBytes) throw new InvalidDataException("Profile file is too large.");
        try
        {
            var result = JsonSerializer.Deserialize<ProfileDocument>(data, Options) ?? throw new InvalidDataException("Empty profile document.");
            result.Validate();
            return result;
        }
        catch (JsonException ex) { throw new InvalidDataException("Malformed profile JSON.", ex); }
        catch (NotSupportedException ex) { throw new InvalidDataException("Unsupported profile data.", ex); }
    }
    public async Task<ProfileDocument> LoadAsync(string path, CancellationToken token = default)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (file.Length > MaximumBytes) throw new InvalidDataException("Profile file is too large.");
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            int read = await file.ReadAsync(chunk, token).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > MaximumBytes) throw new InvalidDataException("Profile file is too large.");
            buffer.Write(chunk, 0, read);
        }
        return Parse(buffer.ToArray());
    }
    public async Task SaveAsync(string path, ProfileDocument document, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        // Serialize a validated snapshot before touching the destination.
        document.Validate();
        var data = JsonSerializer.SerializeToUtf8Bytes(document, Options);
        if (data.Length > MaximumBytes) throw new InvalidDataException("Profile file is too large.");
        // Revalidate the actual serialized snapshot in case a mutable input list changed.
        _ = Parse(data);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new ArgumentException("A directory is required.", nameof(path));
        await _writeGate.WaitAsync(token).ConfigureAwait(false);
        string temp = Path.Combine(directory, ".nexus-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(directory);
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await file.WriteAsync(data, token).ConfigureAwait(false);
                await file.FlushAsync(token).ConfigureAwait(false);
                file.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temp, fullPath, overwrite: true);
        }
        finally
        {
            try { File.Delete(temp); } finally { _writeGate.Release(); }
        }
    }
}
