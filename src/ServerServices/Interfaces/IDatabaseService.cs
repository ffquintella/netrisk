using Model.Database;

namespace ServerServices.Interfaces;

public interface IDatabaseService
{
    public void Init(int initialVersion, int currentVersion);
    public void Backup();
    public void Restore();
    public DatabaseStatus Status();
}