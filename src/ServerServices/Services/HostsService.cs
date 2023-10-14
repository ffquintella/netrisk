using AutoMapper;
using DAL;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class HostsService: ServiceBase, IHostsService
{
    private IMapper Mapper { get; }
    public HostsService(ILogger logger, DALService dalService, IMapper mapper) : base(logger, dalService)
    {
        Mapper = mapper;
    }
    
    public List<Host> GetAll()
    {
        var hosts = new List<Host>();

        using var dbContext = DalService.GetContext();
        
        hosts = dbContext.Hosts.ToList();
        
        return hosts;
    }
    
    public Host GetById(int hostId)
    {
        using var dbContext = DalService.GetContext();

        var host = dbContext.Hosts.Find(hostId);
        
        if( host == null) throw new DataNotFoundException("hosts",hostId.ToString(), new Exception("Host not found"));
        
        return host;
    }

    public void Delete(int hostId)
    {
        using var dbContext = DalService.GetContext();

        var host = dbContext.Hosts.Find(hostId);
        
        if( host == null) throw new DataNotFoundException("hosts",hostId.ToString(), new Exception("Host not found"));
        
        dbContext.Hosts.Remove(host);
        dbContext.SaveChanges();
    }

    public Host Create(Host host)
    {
        host.Id = 0;
        using var dbContext = DalService.GetContext();

        var newHost = dbContext.Hosts.Add(host);
        dbContext.SaveChanges();
        
        return newHost.Entity;
    }

    public Host GetByIp(string hostIp)
    {
        using var dbContext = DalService.GetContext();

        var host = dbContext.Hosts.Where(h => h.Ip == hostIp).FirstOrDefault();
        
        if(host == null) throw new DataNotFoundException("hosts",hostIp, new Exception("Host not found"));

        return host;

    }
    
    public void Update(Host host)
    {
        if(host == null) throw new ArgumentNullException(nameof(host));
        if(host.Id == 0) throw new ArgumentException("Host id cannot be 0");
        
        using var dbContext = DalService.GetContext();
        
        var dbhost = dbContext.Hosts.Find(host.Id);
        
        if( dbhost == null) throw new DataNotFoundException("hosts",host!.Id.ToString(), new Exception("Host not found"));

        Mapper.Map(host, dbhost);
        
        dbContext.SaveChanges();

    }

    public List<DAL.Entities.HostsService> GetHostServices(int hostId)
    {
        if(hostId == 0) throw new ArgumentException("Host id cannot be 0");
        
        using var dbContext = DalService.GetContext();
        
        var dbhost = dbContext.Hosts.Include(h => h.HostsServices).FirstOrDefault(h => h.Id == hostId);
        
        
        if( dbhost == null) throw new DataNotFoundException("hosts",hostId.ToString(), new Exception("Host not found"));

        var services = dbhost.HostsServices.ToList();

        return services;
    }

    public DAL.Entities.HostsService GetHostService(int hostId, int serviceId)
    {
        if(hostId == 0) throw new ArgumentException("Host id cannot be 0");
        
        using var dbContext = DalService.GetContext();
        
        var dbhost = dbContext.Hosts.Include(h => h.HostsServices).FirstOrDefault(h => h.Id == hostId);
        
        
        if( dbhost == null) throw new DataNotFoundException("hosts",hostId.ToString(), new Exception("Host not found"));

        var service = dbhost.HostsServices.FirstOrDefault(hs => hs.Id == serviceId);
        
        if(service == null) throw new DataNotFoundException("hosts_services",serviceId.ToString(), new Exception("Service not found"));

        return service;
    }
    
    public bool HostHasService(int hostId, string name, int? port, string protocol)
    {
        if(hostId == 0) throw new ArgumentException("Host id cannot be 0");
        
        using var dbContext = DalService.GetContext();
        
        var dbhost = dbContext.Hosts.Include(h => h.HostsServices).FirstOrDefault(h => h.Id == hostId);
        if( dbhost == null) throw new DataNotFoundException("hosts",hostId.ToString(), new Exception("Host not found"));
        
        var service = dbhost.HostsServices.FirstOrDefault(s => s.Name == name && s.Port == port && s.Protocol == protocol);
        return service != null;
    }

    public DAL.Entities.HostsService CreateAndAddService(int hostId, DAL.Entities.HostsService service)
    {
        if(hostId == 0) throw new ArgumentException("Host id cannot be 0");
        
        using var dbContext = DalService.GetContext();
        var dbhost = dbContext.Hosts.Include(h => h.HostsServices).FirstOrDefault(h => h.Id == hostId);
        if( dbhost == null) throw new DataNotFoundException("hosts",hostId.ToString(), new Exception("Host not found"));
        
        service.Id = 0;
        
        dbhost.HostsServices.Add(service);
        dbContext.SaveChanges();

        var identifiedService = dbContext.HostsServices.FirstOrDefault(hs => hs.HostId == hostId &&
            hs.Protocol == service.Protocol && hs.Port == service.Port && hs.Name == service.Name);
        return identifiedService!;

    }

    public void DeleteService(int hostId, int serviceId)
    {
        if(hostId == 0) throw new ArgumentException("Host id cannot be 0");
        
        using var dbContext = DalService.GetContext();
        var dbhost = dbContext.Hosts.Include(h => h.HostsServices).FirstOrDefault(h => h.Id == hostId);
        if( dbhost == null) throw new DataNotFoundException("hosts",hostId.ToString(), new Exception("Host not found"));
        
        var service = dbhost.HostsServices.FirstOrDefault(s => s.Id == serviceId);
        if(service == null) throw new DataNotFoundException("hosts_services",serviceId.ToString(), new Exception("Service not found"));
        dbhost.HostsServices.Remove(service);
        dbContext.SaveChanges();
    }

    public void UpdateService(int hostId, DAL.Entities.HostsService service)
    {
        if(hostId == 0) throw new ArgumentException("Host id cannot be 0");
        
        using var dbContext = DalService.GetContext();
        var dbhost = dbContext.Hosts.Include(h => h.HostsServices).FirstOrDefault(h => h.Id == hostId);
        if( dbhost == null) throw new DataNotFoundException("hosts",hostId.ToString(), new Exception("Host not found"));
        
        var dbService = dbhost.HostsServices.FirstOrDefault(s => s.Id == service.Id);
        if(dbService == null) throw new DataNotFoundException("hosts_services",service.Id.ToString(), new Exception("Service not found"));
        
        dbService = Mapper.Map(service, dbService);
        dbService.Host = dbhost;
        dbContext.SaveChanges();
    }
    
}