using Model.Database;

namespace ServerServices.Interfaces;

public interface IDatabaseService
{
    public DatabaseOperationResult Init(int initialVersion, int targetVersion);
    public DatabaseOperationResult Update(int initialVersion, int targetVersion);
    public void Backup();
    public void Restore();
    public DatabaseStatus Status();
}