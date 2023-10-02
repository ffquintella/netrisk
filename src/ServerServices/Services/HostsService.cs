using AutoMapper;
using DAL;
using DAL.Entities;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class HostsService: ServiceBase, IHostsService
{
    private IMapper Mapper { get; }
    public HostsService(ILogger logger, DALManager dalManager, IMapper mapper) : base(logger, dalManager)
    {
        Mapper = mapper;
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

    public void Delete(int hostId)
    {
        using var dbContext = DalManager.GetContext();

        var host = dbContext.Hosts.Find(hostId);
        
        if( host == null) throw new DataNotFoundException("hosts",hostId.ToString(), new Exception("Host not found"));
        
        dbContext.Hosts.Remove(host);
        dbContext.SaveChanges();
    }

    public Host Create(Host host)
    {
        host.Id = 0;
        using var dbContext = DalManager.GetContext();

        var newHost = dbContext.Hosts.Add(host);
        dbContext.SaveChanges();
        
        return newHost.Entity;
    }

    public void Update(Host host)
    {
        if(host == null) throw new ArgumentNullException(nameof(host));
        if(host.Id == 0) throw new ArgumentException("Host id cannot be 0");
        
        using var dbContext = DalManager.GetContext();
        
        var dbhost = dbContext.Hosts.Find(host.Id);
        
        if( dbhost == null) throw new DataNotFoundException("hosts",host!.Id.ToString(), new Exception("Host not found"));

        Mapper.Map(host, dbhost);
        
        dbContext.SaveChanges();

    }

}