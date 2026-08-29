using HTFManager.Core.Models;

namespace HTFManager.Core.Interfaces;

public interface IPackageArtifactStore
{
    PackageArtifact? FindVerifiedArtifact(InstalledMod mod);
    PackageArtifact? FindExactArtifact(ProfileModRequirement requirement);
    void PreserveCurrentArtifact(ModInstallationRecord record);
    void CaptureArtifact(ModInstallationRecord record, string sourcePath);
}
