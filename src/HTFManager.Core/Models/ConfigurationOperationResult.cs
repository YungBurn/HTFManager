namespace HTFManager.Core.Models;

public sealed class ConfigurationOperationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public string? BackupPath { get; init; }

    public static ConfigurationOperationResult Ok(string message = "", string? backupPath = null)
        => new() { Success = true, Message = message, BackupPath = backupPath };

    public static ConfigurationOperationResult Fail(string message)
        => new() { Success = false, Message = message };
}
