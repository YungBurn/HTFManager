using HTFManager.Core.Models;

namespace HTFManager.Core.Interfaces;

public interface ILoaderSetupService
{
    LoaderRecommendation GetRecommendation(ModLoaderKind loader);
    LoaderInstallRecord? GetManagedRecord(ModLoaderKind loader, string? gameDirectory);
    Task<LoaderOperationResult> InstallOrRepairAsync(
        ModLoaderKind loader,
        GameEnvironmentInfo environment,
        bool keepPackageCache = true,
        CancellationToken cancellationToken = default);
    LoaderOperationResult Uninstall(ModLoaderKind loader, GameEnvironmentInfo environment);
    IReadOnlyList<DiagnosticItem> Validate(ModLoaderKind loader, GameEnvironmentInfo environment);
}
