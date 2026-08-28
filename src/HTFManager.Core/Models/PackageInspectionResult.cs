namespace HTFManager.Core.Models;

public sealed class PackageInspectionResult
{
    public bool IsValid { get; init; }
    public string Name { get; init; } = "Unknown Mod";
    public string Version { get; init; } = "—";
    public string Author { get; init; } = "Unknown";
    public string Description { get; init; } = "";
    public string? PackageKey { get; init; }
    public ModSourceType Source { get; init; } = ModSourceType.LocalArchive;
    public ModPackageKind Kind { get; init; } = ModPackageKind.Unknown;
    public ModLoaderKind Loader { get; init; } = ModLoaderKind.Unknown;
    public ModComponentKind Component { get; init; } = ModComponentKind.Unknown;
    public IReadOnlyList<string> Dependencies { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> TargetFiles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Conflicts { get; init; } = Array.Empty<string>();
    public string? Error { get; init; }
    public PackageRiskLevel RiskLevel { get; init; } = PackageRiskLevel.Safe;
    public bool MissingLoader { get; init; }
    public bool IsUpgrade { get; init; }
    public string? ExistingVersion { get; init; }
    public long PackageSize { get; init; }
    public string TargetSummary { get; init; } = "—";
}
