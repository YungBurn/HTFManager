using HTFManager.Core.Models;

namespace HTFManager.Core.Interfaces;

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
    string DataDirectory { get; }
}
