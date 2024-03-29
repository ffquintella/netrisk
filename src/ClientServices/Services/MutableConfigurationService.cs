﻿using Model.Configuration;
using ClientServices.Interfaces;
using LiteDB;
using Model.Authentication;

namespace ClientServices.Services;

public class MutableConfigurationService: IMutableConfigurationService
{
    private IEnvironmentService _environmentService;
    
    private static Mutex mut = new Mutex();

    private string _configurationFilePath;
    private string _configurationConnectionString;
    public MutableConfigurationService(IEnvironmentService environmentService )
    {
        _environmentService = environmentService;
        _configurationFilePath = Path.Combine(_environmentService.ApplicationDataFolder, @"configuration.db");
        _configurationConnectionString = "Filename=" + _configurationFilePath + ";Upgrade=true;Password="+_environmentService.DeviceToken + "-" + _environmentService.DeviceID;
    }

    public bool IsInitialized
    {
        get
        {
            if (_environmentService == null) return false;
            return File.Exists(_configurationFilePath);
        }
    }

    public void Initialize()
    {
        if (!IsInitialized)
        {
            if(!Directory.Exists(_environmentService.ApplicationDataFolder)) Directory.CreateDirectory(_environmentService.ApplicationDataFolder);

            mut.WaitOne();
            using var db = new LiteDatabase(_configurationConnectionString);
            var col = db.GetCollection<MutableConfiguration>("configuration");

            col.Insert(new MutableConfiguration
            {
                ID = 1,
                Name = "DeviceID",
                Value = _environmentService.DeviceID
            });
            col.EnsureIndex(x => x.Name);
            
            mut.ReleaseMutex();
        }
    }

    public string? GetConfigurationValue(string name)
    {
        if (!IsInitialized) Initialize();
        mut.WaitOne();
        using var db = new LiteDatabase(_configurationConnectionString);
        var col = db.GetCollection<MutableConfiguration>("configuration");
        var config = col.FindOne(x => x.Name == name);
        mut.ReleaseMutex();
        if (config == null) return null;
        return config.Value;
    }

    public Task<string?> GetConfigurationValueAsync(string name)
    {
        return Task.Run(() =>
        {
            if (!IsInitialized) Initialize();
            mut.WaitOne();
            using var db = new LiteDatabase(_configurationConnectionString);
            var col = db.GetCollection<MutableConfiguration>("configuration");
            var config = col.FindOne(x => x.Name == name);
            mut.ReleaseMutex();
            if (config == null) return null;
            return config.Value;
        });
    }

    public void SetConfigurationValue(string name, string value)
    {
        if (!IsInitialized) Initialize();
        mut.WaitOne();
        using var db = new LiteDatabase(_configurationConnectionString);
        var col = db.GetCollection<MutableConfiguration>("configuration");

        MutableConfiguration conf = col.FindOne(mo => mo.Name == name);

        if (conf == null)
        {
            col.Insert(new MutableConfiguration
            {
                Name = name,
                Value = value
            });
        }
        else
        {
            conf.Value = value;
            col.Update(conf);
        }
        mut.ReleaseMutex();
    }

    public void RemoveConfigurationValue(string name)
    {
        if (!IsInitialized) Initialize();
        mut.WaitOne();
        using var db = new LiteDatabase(_configurationConnectionString);
        var col = db.GetCollection<MutableConfiguration>("configuration");

        MutableConfiguration conf = col.FindOne(mo => mo.Name == name);

        if (conf != null)
        {
            col.Delete(conf.ID);
        }
        mut.ReleaseMutex();
    }
    
    public void SaveAuthenticatedUser(AuthenticatedUserInfo user)
    {
        if (!IsInitialized) Initialize();
        mut.WaitOne();
        using var db = new LiteDatabase(_configurationConnectionString);
        var col = db.GetCollection<AuthenticatedUserInfo>("authenticatedUser");

        if (!col.Update(user))
        {
            col.Insert(user);
        }
        mut.ReleaseMutex();
        
    }
    
    public AuthenticatedUserInfo? GetAuthenticatedUser()
    {
        if (!IsInitialized) Initialize();
        mut.WaitOne();
        using var db = new LiteDatabase(_configurationConnectionString);
        var col = db.GetCollection<AuthenticatedUserInfo>("authenticatedUser");
        var user = col.FindOne(u => true);
        mut.ReleaseMutex();
        return user ?? null;
    }
}