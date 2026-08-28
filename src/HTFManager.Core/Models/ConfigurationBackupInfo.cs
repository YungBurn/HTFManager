namespace HTFManager.Core.Models;

public sealed class ConfigurationBackupInfo
{
    public required string FilePath { get; init; }
    public required DateTime CreatedUtc { get; init; }
}
