using HTFManager.Core.Models;

namespace HTFManager.Core.Interfaces;

public interface IModPackageService
{
    Task<PackageInspectionResult> InspectAsync(string sourcePath, CancellationToken cancellationToken = default);
    Task<PackageInspectionResult> InspectForInstallAsync(
        string sourcePath,
        GameEnvironmentInfo environment,
        ModInstallMetadata? metadata = null,
        CancellationToken cancellationToken = default);
    Task<ModOperationResult> InstallAsync(
        string sourcePath,
        GameEnvironmentInfo environment,
        ModInstallMetadata? metadata = null,
        bool autoEnable = true,
        bool keepPackageCache = true,
        CancellationToken cancellationToken = default);
    ModOperationResult Uninstall(InstalledMod mod, GameEnvironmentInfo environment, bool preserveConfig = true);
}
