namespace HTFManager.Core.Models;

public sealed class LoaderOperationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public LoaderInstallRecord? Record { get; init; }

    public static LoaderOperationResult Ok(string message, LoaderInstallRecord? record = null)
        => new() { Success = true, Message = message, Record = record };

    public static LoaderOperationResult Fail(string message)
        => new() { Success = false, Message = message };
}
