using DAL;
using DAL.Entities;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class HostsService: ServiceBase, IHostsService
{
    public HostsService(ILogger logger, DALManager dalManager) : base(logger, dalManager)
    {
    }
    
    public List<Host> GetAll()
    {
        var hosts = new List<Host>();

        using var dbContext = DalManager.GetContext();
        
        hosts = dbContext.Hosts.ToList();
        
        return hosts;
    }
    
    public Host GetById(int hostId)
    {
        using var dbContext = DalManager.GetContext();

        var host = dbContext.Hosts.Find(hostId);
        
        if( host == null) throw new DataNotFoundException("hosts",hostId.ToString(), new Exception("Host not found"));
        
        return host;
    }


}