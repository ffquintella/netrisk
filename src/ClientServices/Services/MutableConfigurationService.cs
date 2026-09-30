using Model.Configuration;
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

            WithLock(() =>
            {
                using var db = new LiteDatabase(_configurationConnectionString);
                var col = db.GetCollection<MutableConfiguration>("configuration");

                col.Insert(new MutableConfiguration
                {
                    ID = 1,
                    Name = "DeviceID",
                    Value = _environmentService.DeviceID
                });
                col.EnsureIndex(x => x.Name);
            });
        }
    }

    public string? GetConfigurationValue(string name)
    {
        if (!IsInitialized) Initialize();
        return WithLock(() =>
        {
            using var db = new LiteDatabase(_configurationConnectionString);
            var col = db.GetCollection<MutableConfiguration>("configuration");
            var config = col.FindOne(x => x.Name == name);
            return config?.Value;
        });
    }

    public Task<string?> GetConfigurationValueAsync(string name)
    {
        return Task.Run(() => GetConfigurationValue(name));
    }

    public void SetConfigurationValue(string name, string value)
    {
        if (!IsInitialized) Initialize();
        WithLock(() =>
        {
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
        });
    }

    public void RemoveConfigurationValue(string name)
    {
        if (!IsInitialized) Initialize();
        WithLock(() =>
        {
            using var db = new LiteDatabase(_configurationConnectionString);
            var col = db.GetCollection<MutableConfiguration>("configuration");

            MutableConfiguration conf = col.FindOne(mo => mo.Name == name);

            if (conf != null)
            {
                col.Delete(conf.ID);
            }
        });
    }

    public void SaveAuthenticatedUser(AuthenticatedUserInfo user)
    {
        if (!IsInitialized) Initialize();
        WithLock(() =>
        {
            using var db = new LiteDatabase(_configurationConnectionString);
            var col = db.GetCollection<AuthenticatedUserInfo>("authenticatedUser");

            if (!col.Update(user))
            {
                col.Insert(user);
            }
        });
    }

    public AuthenticatedUserInfo? GetAuthenticatedUser()
    {
        if (!IsInitialized) Initialize();
        return WithLock(() =>
        {
            using var db = new LiteDatabase(_configurationConnectionString);
            var col = db.GetCollection<AuthenticatedUserInfo>("authenticatedUser");
            return col.FindOne(u => true);
        });
    }

    /// <summary>
    /// Runs <paramref name="action"/> under the shared LiteDB mutex, releasing it even when the action
    /// throws.
    ///
    /// Every caller used to sandwich its own <c>WaitOne()</c>/<c>ReleaseMutex()</c> pair around the
    /// LiteDB call with no <c>try</c>/<c>finally</c>. The first time any of them threw — a concurrent
    /// open of the same file from two threads is exactly the case this mutex exists to serialize
    /// against — the mutex was abandoned and never released. The next caller's <c>WaitOne()</c> then
    /// throws <see cref="AbandonedMutexException"/> *to a method with the same missing
    /// <c>try</c>/<c>finally</c>*, so it abandons the mutex again before it can reach its own
    /// <c>ReleaseMutex()</c> — every config read/write after that point fails the same way forever,
    /// including the auth token, which is how one bad LiteDB open turned into an unrecoverable
    /// "Unauthorized" for the rest of the session.
    /// </summary>
    private static void WithLock(Action action) => WithLock<object?>(() =>
    {
        action();
        return null;
    });

    private static T WithLock<T>(Func<T> func)
    {
        try
        {
            mut.WaitOne();
        }
        catch (AbandonedMutexException)
        {
            // Ownership was still granted to this call despite the exception — a previous holder
            // died without releasing. Proceed instead of propagating: this is the recovery step that
            // was missing, not a new failure.
        }

        try
        {
            return func();
        }
        finally
        {
            mut.ReleaseMutex();
        }
    }
}