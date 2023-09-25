using Microsoft.Extensions.Configuration;
using Model.Database;
using MySqlConnector;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class DatabaseService: IDatabaseService
{
    private IConfiguration Configuration { get; }
    private ILogger Logger { get; }
    public DatabaseService(IConfiguration configuration, ILogger logger)
    {
        Configuration = configuration;
        Logger = logger;
    }
    
    public void Init(int initialVersion, int currentVersion)
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
        Logger.Debug("Database Status requested");
        
        var dbStatus = new DatabaseStatus()
        {
            Message = "Database did not respond",
            Status = "Offline"
        };

        try
        {
            var connectionString = Configuration["Database:ConnectionString"];
            
            using var connection = new MySqlConnection(connectionString);
            connection.Open();

            if (connection.Ping())
            {

                dbStatus.Message = "Connection established";
                dbStatus.Status = "Online";
                dbStatus.ServerVersion = connection.ServerVersion;

                var schema = connection.Database;
                
                using var command = new MySqlCommand($"SELECT SCHEMA_NAME FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = '{schema}'", connection);
                using var reader = command.ExecuteReader();
                
                var schemaExists = reader.HasRows;
                
                if(schemaExists) dbStatus.Message = "Schema Exists";
                else
                {
                    dbStatus.Message = "Schema does not exist";
                    return dbStatus;
                }
                
                reader.Close();
                
                using var command2 = new MySqlCommand(
                    "SELECT * FROM information_schema.tables " +
                    $"WHERE table_schema = '{schema}' " +
                    $"AND table_name = 'settings' LIMIT 1;", connection);
                
                using var reader2 = command2.ExecuteReader();
                
                var tableExists = reader2.HasRows;
                
                if(tableExists) dbStatus.Message = "Tables Exists";
                else
                {
                    dbStatus.Message = "Tables does not exist";
                    return dbStatus;
                }
                reader2.Close();
                
                using var command3 = new MySqlCommand(
                    "select value from settings where name='db_version';", connection);
                
                using var reader3 = command3.ExecuteReader();

                if(reader3.Read())
                {
                    dbStatus.Message = "All good";
                    dbStatus.Version = reader3.GetString(0);
                }

                reader3.Close();
            }
            


        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Database status error");
        }

        return dbStatus;
        
    }
}