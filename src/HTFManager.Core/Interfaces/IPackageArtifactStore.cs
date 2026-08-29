using HTFManager.Core.Models;

namespace HTFManager.Core.Interfaces;

public interface IPackageArtifactStore
{
    PackageArtifact? FindVerifiedArtifact(InstalledMod mod);
}
