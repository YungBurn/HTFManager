using HTFManager.Core.Models;

namespace HTFManager.Core.Interfaces;

public interface IModCatalogService
{
    Task<IReadOnlyList<RemoteModPackage>> GetPackagesAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
    Task<string> DownloadPackageAsync(RemoteModPackage package, RemoteModVersion version, CancellationToken cancellationToken = default);
}
