namespace HTFManager.Core.Models;

public enum ProfileRestoreDisposition
{
    Ready,
    VersionFallback,
    PackageUnavailable,
    CatalogUnavailable,
    ManualRequired
}
