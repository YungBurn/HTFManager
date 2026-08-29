using HTFManager.Core.Models;

namespace HTFManager.Core.Interfaces;

public interface IApplicationUpdateService
{
    Task<ApplicationUpdateInfo> CheckAsync(string currentVersion, CancellationToken cancellationToken = default);
    Task<ApplicationUpdateInfo> DownloadAsync(ApplicationUpdateInfo update, CancellationToken cancellationToken = default);
}
