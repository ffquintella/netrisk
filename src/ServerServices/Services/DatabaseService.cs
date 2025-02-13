﻿using System;
using System.IO;
using System.Net.Mime;
using System.Reflection;
using System.Text;
using System.Text.Unicode;
using Microsoft.Extensions.Configuration;
using MigraDoc.DocumentObjectModel;
using Model.Database;
using MySqlConnector;
using Serilog;
using ServerServices.Interfaces;
using Tools.Criptography;
using MySqlCommand = MySqlConnector.MySqlCommand;
using MySqlConnection = MySqlConnector.MySqlConnection;

namespace ServerServices.Services;

public class DatabaseService(
    IConfiguration configuration,
    ILogger logger,
    IConfigurationsService configurationsService,
    IDalService dalService)
    : IDatabaseService
{
    private IConfiguration Configuration { get; } = configuration;
    private ILogger Logger { get; } = logger;
    private IConfigurationsService ConfigurationsService { get; } = configurationsService;

    private IDalService DalService { get; } = dalService;

    public int FixData(string operation)
    {
        using var context = DalService.GetContext();
        
        switch (operation)
        {
            case "riskCatalog":
                var risks = context.Risks.ToList();
                foreach (var risk in risks)
                {
                    var oldCatalogs = risk.RiskCatalogMapping.Split(',');
                    if(oldCatalogs.Length == 0) continue;
                    
                    foreach (var oldCatalogStrId in oldCatalogs)
                    {
                        if(oldCatalogStrId.IsValueNullOrEmpty()) continue;
                        var oldId = Int32.Parse(oldCatalogStrId);
                        var newCatalog = context.RiskCatalogs.FirstOrDefault(rc => rc.Id == oldId);
                        if (newCatalog == null) continue;
                        risk.RiskCatalogs.Add(newCatalog);
                    }
                    
                    context.Risks.Update(risk);
                }
                context.SaveChanges();
                return 0;
            default:
                return -1;
        }
        
        
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
        
        int currentDbVersion = 0;
        
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
            int dbVersion = i + 1;
                
            var sql = File.ReadAllText(Path.Combine(currentDir!, "DB", "Structure", $"{dbVersion}.sql"));
            
            using var createCmd = new MySqlCommand(sql, connection);

            var readerCreate = createCmd.ExecuteNonQuery();
            
            if(readerCreate != -1)
            {
                result.Message = "Structure created";
                result.Code = 30;
                result.Status = "Success";
            }

                
            // Now let´s add the data
            var sql2 = File.ReadAllText(Path.Combine(currentDir!, "DB", "Data", $"{dbVersion}.sql"));
                
            using var dataCmd = new MySqlCommand(sql2, connection);
                
            var readerResult = dataCmd.ExecuteNonQuery();

            if (readerResult != -1)
            {
                result.Message = "Data imported created";
                result.Code = 40;
                result.Status = "Success";    
            }
                
        }
        
        
        
        return result;

    }

    public DatabaseOperationResult Update(int initialVersion, int targetVersion)
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

        // Check if database has structure created. If not, STOP.
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
        
        int currentDbVersion = 0;
        
        if (!tableExists)
        {
            result.Code = 50;
            result.Message = "Database is empty ";
            return result;
        }
        
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
                
        }

        reader2.Close();
        
        
        for (int i = currentDbVersion; i < targetVersion; i++)
        {
            //Lets create the tables
            int dbVersion = i + 1;
                
            var sql = File.ReadAllText(Path.Combine(currentDir!, "DB", "Structure", $"{dbVersion}.sql"));

            if (sql.Length != 0)
            {
                using var createCmd = new MySqlCommand(sql, connection);

                var readerCreate = createCmd.ExecuteNonQuery();
            
                if(readerCreate != -1)
                {
                    result.Message = "Structure altered";
                    result.Code = 30;
                    result.Status = "Success";
                }
            } 
            
                
            // Now let´s adjust the data
            var sql2 = File.ReadAllText(Path.Combine(currentDir!, "DB", "Data", $"{dbVersion}.sql"));
            
            if(sql2.Length == 0) continue;
                
            using var dataCmd = new MySqlCommand(sql2, connection);
                
            var readerResult = dataCmd.ExecuteNonQuery();

            if (readerResult != -1)
            {
                result.Message = "Data altered";
                result.Code = 40;
                result.Status = "Success";    
            }
                
        }
        
        
        
        return result;
    }
    
    private string GetBackupPath(string destinationDir)
    {

        var encrypted = !string.IsNullOrEmpty( ConfigurationsService.GetBackupPassword() );
        
        Directory.CreateDirectory(destinationDir);
        
        var backupPath = Path.Combine(destinationDir, $"backup_{DateTime.Now:yyyyMMddHHmmss}.sql");
        if(encrypted) backupPath += ".enc";
        
        int i = 1;

        while (File.Exists(backupPath))
        {
            backupPath = Path.Combine(destinationDir, $"backup_{DateTime.Now:yyyyMMddHHmmss}_{i++}.sql");
            if(encrypted) backupPath += ".enc";
        }
        
        
        return backupPath;
    }
    
    public void Backup(string destinationDir = @"/backups")
    {
        Logger.Debug("Database Backup requested");

        if (!Directory.Exists(destinationDir))
        {
            Log.Error("Backup directory not found");
            return;
        }
        
        var encrypted = !string.IsNullOrEmpty( ConfigurationsService.GetBackupPassword() );
        try
        {
            var connectionString = Configuration["Database:ConnectionString"];
            
            string file = GetBackupPath(destinationDir);
            using (var conn = new MySqlConnection(connectionString))
            {
                using (var cmd = new MySqlCommand())
                {
                    cmd.CommandTimeout = 0;
                    using (var mb = new MySqlBackup(cmd))
                    {
                        cmd.Connection = conn;
                        conn.Open();

                        if (encrypted)
                        {
                            var memoryStream = new MemoryStream();
                            mb.ExportToStream(memoryStream);
                            
                            var pwd = ConfigurationsService.GetBackupPassword();

                            //var strdata = Encoding.UTF8.GetString(memoryStream.ToArray());
                            
                            var result = AES.EncryptStream(memoryStream, pwd);
                            
                            //var result = AES.Encrypt(strdata, pwd);
                        
                            using var filer = File.Open(file, FileMode.OpenOrCreate);
                            var bytes = Encoding.UTF8.GetBytes(result);
                            filer.WriteAsync(bytes, 0, bytes.Length);
                            
                            filer.Close();
                        }
                        else
                        {
                            //var backupData = mb.ExportToString(); 
                            mb.ExportToFile(file);
                        }
                        
                        conn.Close();
                    }
                }
            }

        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Database Backup error");
        }
    }

    public void Restore(string sourceFile, string backupPwd = "")
    {
        Logger.Debug("Database Restore requested");
        //var encrypted = !string.IsNullOrEmpty( ConfigurationsService.GetBackupPassword() );
        var encrypted = !string.IsNullOrEmpty(backupPwd);
        if(!sourceFile.StartsWith('/')) sourceFile = Path.Combine(@"/backups", sourceFile);
        
        
        if (!File.Exists(sourceFile)) throw new Exception("File does not exist");
        
        try
        {
            var connectionString = Configuration["Database:ConnectionString"];
            
            using var conn = new MySqlConnection(connectionString);
            using var cmd = new MySqlCommand();
            using var mb = new MySqlBackup(cmd);
            
            cmd.CommandTimeout = 0;
            cmd.Connection = conn;
            conn.Open();
            

            if (encrypted)
            {
                using var stream = new MemoryStream();

                //var fileData = File.ReadAllText(sourceFile);

                var fileBytes = File.ReadAllBytes(sourceFile);
                
                var data = Encoding.UTF8.GetString(fileBytes);
                
                var openData = AES.Decrypt(data, backupPwd);
                
                var bytes = Encoding.UTF8.GetBytes(openData);
                stream.Write(bytes, 0, bytes.Length);
                
                mb.ImportFromStream(stream);
                
            }else mb.ImportFromFile(sourceFile);
            conn.Close();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Database restore error:" + ex.Message);
        }
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