using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DAL;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace API.Tools;

public  class SelfTest: IHostedService
{
    private DALManager _dalManager;
    
    public SelfTest(DALManager dalManager)
    {
        _dalManager = dalManager;
    }
    
    public  void ExecuteAllTests()
    {
        Log.Information("Executing self tests");
        this.ExecuteDBTests();
    }

    public  void ExecuteDBTests()
    {
        var context = _dalManager.GetContext();

        if (!context.Database.CanConnect())
        {
            Log.Error("Error in self test: database connection failed");
        }
        else
        {
            Log.Information("DB connection successful");
        }

    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        
        ExecuteAllTests();
        
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Log.Information("Application Stopped");
        
        return Task.CompletedTask;
    }
}