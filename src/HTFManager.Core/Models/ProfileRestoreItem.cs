namespace HTFManager.Core.Models;

public sealed class ProfileRestoreItem
{
    public required ProfileModRequirement Requirement { get; init; }
    public ProfileRestoreDisposition Disposition { get; init; }
    public RemoteModPackage? RemotePackage { get; init; }
    public RemoteModVersion? SelectedVersion { get; init; }
    public string Message { get; init; } = "";

    public string RequestedVersion => Requirement.Version;
    public string? SelectedVersionNumber => SelectedVersion?.VersionNumber;
    public bool IsInstallable => Disposition is ProfileRestoreDisposition.Ready or ProfileRestoreDisposition.VersionFallback;
    public bool UsesVersionFallback => Disposition == ProfileRestoreDisposition.VersionFallback;
}
