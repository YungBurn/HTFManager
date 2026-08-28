namespace HTFManager.Core.Models;

public sealed class ModOperationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public ModInstallationRecord? Installation { get; init; }
    public PackageInspectionResult? Inspection { get; init; }

    public static ModOperationResult Ok(string message, ModInstallationRecord? installation = null, PackageInspectionResult? inspection = null)
        => new() { Success = true, Message = message, Installation = installation, Inspection = inspection };

    public static ModOperationResult Fail(string message, PackageInspectionResult? inspection = null)
        => new() { Success = false, Message = message, Inspection = inspection };
}
