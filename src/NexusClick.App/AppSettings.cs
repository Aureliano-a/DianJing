using System.IO;
using System.Text.Json;

namespace NexusClick.App;

public sealed record AppSettings
{
    public int Version { get; init; } = 1;
    public int StartKey { get; init; } = 0x77;
    public int TargetKey { get; init; } = 0x41;
    public string KeyName { get; init; } = "A";
    public string Mode { get; init; } = "鼠标";
    public string Button { get; init; } = "左键";
    public string Trigger { get; init; } = "切换触发";
    public string Frequency { get; init; } = "10";
    public string Hold { get; init; } = "30";
    public string Repeats { get; init; } = "0";
    public string MaxDuration { get; init; } = "0";
    public string RandomInterval { get; init; } = "0";
    public string BurstCount { get; init; } = "0";
    public string BurstRest { get; init; } = "1000";
    public string X { get; init; } = "0";
    public string Y { get; init; } = "0";
    public bool Fixed { get; init; }
    public bool Animation { get; init; } = true;
}

public static class AppSettingsStore
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DianJing", "settings.json");
    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 16384) return new();
            var value=JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
            if (value is null || value.Version != 1 || value.StartKey is < 0x70 or > 0x79 ||
                value.TargetKey is < 1 or > 254 || value.TargetKey is 0x77 or 0x7A or 0x7B ||
                value.TargetKey == value.StartKey || value.Mode is not ("鼠标" or "键盘") ||
                value.Button is not ("左键" or "右键" or "中键") || value.Trigger is not ("切换触发" or "按住触发") ||
                new[] { value.KeyName, value.Frequency, value.Hold, value.Repeats, value.MaxDuration, value.RandomInterval, value.BurstCount, value.BurstRest, value.X, value.Y }.Any(x => x is null || x.Length > 64))
                return new();
            return value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public static void Save(string path, AppSettings value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { File.WriteAllText(temp,JsonSerializer.Serialize(value)); File.Move(temp,path,true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
