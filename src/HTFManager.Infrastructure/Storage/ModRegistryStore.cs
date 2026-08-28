using System.Text.Json;
using HTFManager.Core.Models;

namespace HTFManager.Infrastructure.Storage;

public sealed class ModRegistryStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ModRegistryStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _path = Path.Combine(dataDirectory, "installations.json");
    }

    public IReadOnlyList<ModInstallationRecord> LoadAll()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path)) return Array.Empty<ModInstallationRecord>();
                return JsonSerializer.Deserialize<List<ModInstallationRecord>>(File.ReadAllText(_path), JsonOptions)
                       ?? new List<ModInstallationRecord>();
            }
            catch
            {
                return Array.Empty<ModInstallationRecord>();
            }
        }
    }

    public ModInstallationRecord? Find(string id)
        => LoadAll().FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public ModInstallationRecord? FindByPackageKey(string gameDirectory, string packageKey)
        => LoadAll().FirstOrDefault(x =>
            SamePath(x.GameDirectory, gameDirectory) &&
            string.Equals(x.PackageKey, packageKey, StringComparison.OrdinalIgnoreCase));

    public ModInstallationRecord? FindOwner(string gameDirectory, string relativePath)
    {
        var normalized = NormalizeRelative(relativePath);
        return LoadAll().FirstOrDefault(x =>
            SamePath(x.GameDirectory, gameDirectory) &&
            x.Files.Any(f => string.Equals(NormalizeRelative(f), normalized, StringComparison.OrdinalIgnoreCase)));
    }

    public void Save(ModInstallationRecord record)
    {
        lock (_gate)
        {
            var items = ReadUnlocked();
            var index = items.FindIndex(x => x.Id.Equals(record.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) items[index] = record;
            else items.Add(record);
            WriteUnlocked(items);
        }
    }

    public void Delete(string id)
    {
        lock (_gate)
        {
            var items = ReadUnlocked();
            items.RemoveAll(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            WriteUnlocked(items);
        }
    }

    private List<ModInstallationRecord> ReadUnlocked()
    {
        try
        {
            if (!File.Exists(_path)) return new List<ModInstallationRecord>();
            return JsonSerializer.Deserialize<List<ModInstallationRecord>>(File.ReadAllText(_path), JsonOptions)
                   ?? new List<ModInstallationRecord>();
        }
        catch
        {
            return new List<ModInstallationRecord>();
        }
    }

    private void WriteUnlocked(List<ModInstallationRecord> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(items, JsonOptions));
        File.Move(temp, _path, true);
    }

    private static bool SamePath(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string NormalizeRelative(string path)
        => path.Replace('\\', '/').TrimStart('/');
}
