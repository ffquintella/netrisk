using System.Reflection;
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
    
    public DatabaseOperationResult Init(int initialVersion, int targetVersion)
    {
        var result = new DatabaseOperationResult()
        {
            Code = -1,
            Message = "Database did not respond",
            Status = "Error"
        };

        var status = Status();
        
        if(status.Status != "Online")
        {
            result.Code = 10;
            result.Message = "Database is offline";
            return result;
        }

        // Check if database has structure created. If not, create it.
        var connectionString = Configuration["Database:ConnectionString"];
        
        using var connection = new MySqlConnection(connectionString);
        connection.Open();
        
        var schema = connection.Database;
        
        using var command = new MySqlCommand(
            "SELECT * FROM information_schema.tables " +
            $"WHERE table_schema = '{schema}' " +
            $"AND table_name = 'settings' LIMIT 1;", connection);
                
        using var reader = command.ExecuteReader();
                
        var tableExists = reader.HasRows;
        reader.Close();
        
        var currentDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        
        int currentDbVersion = 1;
        
        if (tableExists)
        {
            result.Code = 20;
            result.Message = "Database already has tables";
            
            // Check version of the database
        
            using var command2 = new MySqlCommand(
                "select value from settings where name='db_version';", connection);
        
            using var reader2 = command2.ExecuteReader();

            
        
            if(reader2.Read())
            {
                var isNumeric = int.TryParse(reader2.GetString(0), out currentDbVersion);
                
                if(!isNumeric) throw new Exception("Database version is not numeric");
                
                //currentDbVersion = Int32.Parse(reader2.GetString(0));
            }

            reader2.Close();
            
        }
        
        for (int i = currentDbVersion; i < targetVersion; i++)
        {
            //Lets create the tables
                
            var sql = File.ReadAllText(Path.Combine(currentDir!, "DB", "Structure", $"{i}.sql"));
            
            using var createCmd = new MySqlCommand(sql, connection);

            using var readerCreate = createCmd.ExecuteReader();
            
            if(readerCreate.Read())
            {
                result.Message = readerCreate.GetString(0);
                result.Code = 30;
                result.Status = "Success";
            }
            readerCreate.Close();
                
            // Now let´s add the data
            var sql2 = File.ReadAllText(Path.Combine(currentDir!, "DB", "Data", $"{i}.sql"));
                
            using var dataCmd = new MySqlCommand(sql2, connection);
                
            var readerResult = dataCmd.ExecuteNonQuery();

            if (readerResult != -1)
            {
                    
            }
                
        }
        
        
        
        return result;

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
                    connection.Close();
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
                    connection.Close();
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
            
            connection.Close();

        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Database status error");
        }

        return dbStatus;
        
    }
}