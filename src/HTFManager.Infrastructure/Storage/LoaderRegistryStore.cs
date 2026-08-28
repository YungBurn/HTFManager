using System.Text.Json;
using HTFManager.Core.Models;

namespace HTFManager.Infrastructure.Storage;

public sealed class LoaderRegistryStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public LoaderRegistryStore(string dataDirectory)
    {
        var directory = Path.Combine(dataDirectory, "loaders");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "installations.json");
    }

    public IReadOnlyList<LoaderInstallRecord> LoadAll()
    {
        lock (_gate) return ReadUnlocked();
    }

    public LoaderInstallRecord? Find(ModLoaderKind loader, string gameDirectory)
        => LoadAll().FirstOrDefault(x => x.Loader == loader && SamePath(x.GameDirectory, gameDirectory));

    public void Save(LoaderInstallRecord record)
    {
        lock (_gate)
        {
            var records = ReadUnlocked();
            records.RemoveAll(x => x.Loader == record.Loader && SamePath(x.GameDirectory, record.GameDirectory));
            records.Add(record);
            WriteUnlocked(records);
        }
    }

    public void Delete(ModLoaderKind loader, string gameDirectory)
    {
        lock (_gate)
        {
            var records = ReadUnlocked();
            records.RemoveAll(x => x.Loader == loader && SamePath(x.GameDirectory, gameDirectory));
            WriteUnlocked(records);
        }
    }

    private List<LoaderInstallRecord> ReadUnlocked()
    {
        try
        {
            if (!File.Exists(_path)) return new List<LoaderInstallRecord>();
            return JsonSerializer.Deserialize<List<LoaderInstallRecord>>(File.ReadAllText(_path), JsonOptions)
                   ?? new List<LoaderInstallRecord>();
        }
        catch { return new List<LoaderInstallRecord>(); }
    }

    private void WriteUnlocked(List<LoaderInstallRecord> records)
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(records, JsonOptions));
        File.Move(temp, _path, true);
    }

    private static bool SamePath(string left, string right)
    {
        try
        {
            return string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }
        catch { return string.Equals(left, right, StringComparison.OrdinalIgnoreCase); }
    }
}
