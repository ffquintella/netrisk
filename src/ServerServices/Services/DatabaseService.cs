using Model.Database;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class DatabaseService: IDatabaseService
{
    public void Init()
    {
        throw new System.NotImplementedException();
    }
    public void Backup()
    {
        throw new System.NotImplementedException();
    }
    public void Restore()
    {
        throw new System.NotImplementedException();
    }
    public DatabaseStatus Status()
    {
        var dbStatus = new DatabaseStatus()
        {
            Message = "Database did not respond",
            Status = "Offline"
        };


        return dbStatus;
        
    }
}