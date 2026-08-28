namespace HTFManager.Core.Models;

public sealed class ProfileOperationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public int ConfigurationCount { get; init; }
    public string? RecoveryPath { get; init; }

    public static ProfileOperationResult Ok(string message, int configurationCount = 0, string? recoveryPath = null)
        => new() { Success = true, Message = message, ConfigurationCount = configurationCount, RecoveryPath = recoveryPath };

    public static ProfileOperationResult Fail(string message, string? recoveryPath = null)
        => new() { Success = false, Message = message, RecoveryPath = recoveryPath };
}
