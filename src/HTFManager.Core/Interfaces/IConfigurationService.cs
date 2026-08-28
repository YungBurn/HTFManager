using HTFManager.Core.Models;

namespace HTFManager.Core.Interfaces;

public interface IConfigurationService
{
    IReadOnlyList<ModConfigurationDocument> Scan(GameEnvironmentInfo environment, IReadOnlyList<InstalledMod> mods);
    ConfigurationOperationResult Save(ModConfigurationDocument document, bool createBackup, int maxBackups);
    IReadOnlyList<ConfigurationBackupInfo> GetBackups(ModConfigurationDocument document);
    ConfigurationOperationResult RestoreLatest(ModConfigurationDocument document, int maxBackups);
}
