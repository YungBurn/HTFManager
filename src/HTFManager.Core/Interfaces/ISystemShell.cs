namespace HTFManager.Core.Interfaces;

public interface ISystemShell
{
    void OpenPath(string path);
    void OpenFile(string path);
    void OpenUri(string uri);
}
