using System.Linq.Expressions;
using AutoMapper;
using DAL;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;
using Sieve.Models;
using Sieve.Services;

namespace ServerServices.Services;

public class HostsService: ServiceBase, IHostsService
{
    private IMapper Mapper { get; }
    private ISieveProcessor SieveProcessor { get; } 
    public HostsService(ILogger logger, ISieveProcessor sieveProcessor, IDalService dalService, IMapper mapper) : base(logger, dalService)
    {
        Mapper = mapper;
        SieveProcessor = sieveProcessor;
    }

    public async Task<bool> HostExistsAsync(string hostIp)
    {
        await using var dbContext = DalService.GetContext();
        
        var host = await dbContext.Hosts.FirstOrDefaultAsync(h => h.Ip == hostIp);
        if(host == null) return false;
        return true;

    }
    
    public List<Host> GetAll()
    {
        var hosts = new List<Host>();

        using var dbContext = DalService.GetContext();
        
        hosts = dbContext.Hosts.ToList();
        
        return hosts;
    }
    
    public async Task<Tuple<List<Host>,int>> GetFiltredAsync(SieveModel sieveModel)
    {
        await using var dbContext = DalService.GetContext();
        
        var result = dbContext.Hosts.AsNoTracking(); // Makes read-only queries faster
         
        var hosts = SieveProcessor.Apply(sieveModel, result, applyPagination: false);
        var totalCount = hosts.Count();
        
        result = SieveProcessor.Apply(sieveModel, result); // Returns `result` after applying the sort/filter/page query in `SieveModel` to it
        
        return new Tuple<List<Host>, int>(result.ToList(), totalCount);
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

    public async Task<Host> CreateAsync(Host host)
    {
        host.Id = 0;
        await using var dbContext = DalService.GetContext();

        var newHost = dbContext.Hosts.Add(host);
        await dbContext.SaveChangesAsync();
        
        return newHost.Entity;
    }

    public Host GetByIp(string hostIp)
    {
        using var dbContext = DalService.GetContext();

        var host = dbContext.Hosts.Where(h => h.Ip == hostIp).FirstOrDefault();
        
        if(host == null) throw new DataNotFoundException("hosts",hostIp, new Exception("Host not found"));

        return host;

    }

    public async Task<Host> GetByIpAsync(string hostIp)
    {
        await using var dbContext = DalService.GetContext();

        var host = await dbContext.Hosts.Where(h => h.Ip == hostIp)
            .AsNoTracking()
            .FirstOrDefaultAsync();
        
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

    public async Task UpdateAsync(Host host)
    {
        if(host == null) throw new ArgumentNullException(nameof(host));
        if(host.Id == 0) throw new ArgumentException("Host id cannot be 0");
        
        await using var dbContext = DalService.GetContext();
        
        var dbhost = await dbContext.Hosts.FindAsync(host.Id);
        
        if( dbhost == null) throw new DataNotFoundException("hosts",host!.Id.ToString(), new Exception("Host not found"));

        Mapper.Map(host, dbhost);
        
        await dbContext.SaveChangesAsync();
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

    public List<Vulnerability> GetVulnerabilities(int hostId)
    {
        if(hostId == 0) throw new ArgumentException("Host id cannot be 0");
        
        using var dbContext = DalService.GetContext();
        
        var dbHost = dbContext.Hosts.Include(h => h.Vulnerabilities).FirstOrDefault(h => h.Id == hostId);
        
        
        if( dbHost == null) throw new DataNotFoundException("hosts",hostId.ToString(), new Exception("Host not found"));

        var vulnerabilities = dbHost.Vulnerabilities.ToList();

        return vulnerabilities;
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

    public async Task<bool> HostHasServiceAsync(int hostId, string name, int? port, string protocol)
    {
        if(hostId == 0) throw new ArgumentException("Host id cannot be 0");
        
        await using var dbContext = DalService.GetContext();
        
        var dbhost = await dbContext.Hosts.Include(h => h.HostsServices).FirstOrDefaultAsync(h => h.Id == hostId);
        if( dbhost == null) throw new DataNotFoundException("hosts",hostId.ToString(), new Exception("Host not found"));
        
        var service = dbhost.HostsServices.FirstOrDefault(s => s.Name == name && s.Port == port && s.Protocol == protocol);
        return service != null;
    }
    
    public DAL.Entities.HostsService FindService(int hostId, Expression<Func<DAL.Entities.HostsService,bool>> expression)
    {
        if(hostId == 0) throw new ArgumentException("Host id cannot be 0");
        
        using var dbContext = DalService.GetContext();
        
        var dbhost = dbContext.Hosts.Include(h => h.HostsServices).FirstOrDefault(h => h.Id == hostId);
        if( dbhost == null) throw new DataNotFoundException("hosts",hostId.ToString(), new Exception("Host not found"));
        
        var service = dbhost.HostsServices.FirstOrDefault(expression.Compile());
        if(service == null) throw new DataNotFoundException("hosts_services",expression.ToString(), new Exception("Service not found"));
        return service;
    }

    public async Task<DAL.Entities.HostsService> FindServiceAsync(int hostId, Expression<Func<DAL.Entities.HostsService, bool>> expression)
    {
        if(hostId == 0) throw new ArgumentException("Host id cannot be 0");
        
        await using var dbContext = DalService.GetContext();
        
        var dbhost = await dbContext.Hosts
            .AsNoTracking()
            .Include(h => h.HostsServices).FirstOrDefaultAsync(h => h.Id == hostId);
        if( dbhost == null) throw new DataNotFoundException("hosts",hostId.ToString(), new Exception("Host not found"));
        
        var service = dbhost.HostsServices
            .FirstOrDefault(expression.Compile());
        if(service == null) throw new DataNotFoundException("hosts_services",expression.ToString(), new Exception("Service not found"));
        return service;
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

    public async Task<DAL.Entities.HostsService> CreateAndAddServiceAsync(int hostId, DAL.Entities.HostsService service)
    {
        if(hostId == 0) throw new ArgumentException("Host id cannot be 0");
        
        await using var dbContext = DalService.GetContext();
        var dbhost = dbContext.Hosts.Include(h => h.HostsServices).FirstOrDefault(h => h.Id == hostId);
        if( dbhost == null) throw new DataNotFoundException("hosts",hostId.ToString(), new Exception("Host not found"));
        
        service.Id = 0;
        
        dbhost.HostsServices.Add(service);
        await dbContext.SaveChangesAsync();

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