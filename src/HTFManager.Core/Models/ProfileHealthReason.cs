namespace HTFManager.Core.Models;

public enum ProfileHealthReason
{
    None,
    ExpectedIdentityNotInstalled,
    ExpectedVersionDiffers,
    InstalledVersionUnknown,
    AmbiguousIdentity,
    LegacyMetadataUnavailable,
    LegacyBindingMissing
}
