namespace HTFManager.Core.Interfaces;

public interface IGameLocator
{
    string? LocateGameDirectory(string? preferredPath = null);
}
