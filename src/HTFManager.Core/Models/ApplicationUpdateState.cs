namespace HTFManager.Core.Models;

public enum ApplicationUpdateState
{
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    Ready,
    Error
}
