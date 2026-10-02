using System.Globalization;
using System.Linq.Expressions;
using Contracts.Importers;
using Mapster;
using DAL;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.DTO;
using Model.Exceptions;
using Model.Status;
using Serilog;
using ServerServices.Interfaces;

using ServerServices.Filtering;
namespace ServerServices.Services;

public class HostsService: ServiceBase, IHostsService
{
    private IEntityFilterMapperProvider FilterMappers { get; }
    public HostsService(ILogger logger, IEntityFilterMapperProvider filterMappers, IDalService dalService) : base(logger, dalService)
    {
        FilterMappers = filterMappers;
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
    
    public async Task<Tuple<List<Host>,int>> GetFiltredAsync(ListQuery query)
    {
        await using var dbContext = DalService.GetContext();

        var result = dbContext.Hosts.AsNoTracking(); // Makes read-only queries faster

        var (rows, totalCount) = result.ApplyListQuery(query, FilterMappers.For<Host>());

        return new Tuple<List<Host>, int>(rows, totalCount);
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

        host.Adapt(dbhost);
        
        dbContext.SaveChanges();

    }

    public async Task UpdateAsync(Host host)
    {
        if(host == null) throw new ArgumentNullException(nameof(host));
        if(host.Id == 0) throw new ArgumentException("Host id cannot be 0");
        
        await using var dbContext = DalService.GetContext();
        
        var dbhost = await dbContext.Hosts.FindAsync(host.Id);
        
        if( dbhost == null) throw new DataNotFoundException("hosts",host!.Id.ToString(), new Exception("Host not found"));

        host.Adapt(dbhost);
        
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
        
        service.Adapt(dbService);
        dbService.Host = dbhost;
        dbContext.SaveChanges();
    }

    public async Task<List<string>> GetEnvironmentsAsync()
    {
        await using var dbContext = DalService.GetContext();

        // Through the scoped Hosts set, so a caller sees only the environments of hosts they can
        // see. Blank values are not an environment anybody can pick, so they are not offered.
        return await dbContext.Hosts
            .AsNoTracking()
            .Where(h => h.Environment != null && h.Environment.Trim() != "")
            .Select(h => h.Environment!)
            .Distinct()
            .OrderBy(e => e)
            .ToListAsync();
    }

    public async Task<HostVulnerabilitySummaryDto> GetVulnerabilitySummaryAsync(int hostId)
    {
        var summaries = await GetVulnerabilitySummariesAsync([hostId]);

        return summaries.Count == 1
            ? summaries[0]
            : throw new DataNotFoundException("hosts", hostId.ToString(), new Exception("Host not found"));
    }

    public async Task<List<HostVulnerabilitySummaryDto>> GetVulnerabilitySummariesAsync(
        IReadOnlyCollection<int> hostIds)
    {
        ArgumentNullException.ThrowIfNull(hostIds);

        var requested = hostIds.Distinct().ToList();

        if (requested.Count > IHostsService.MaxSummaryBatchSize)
            throw new ArgumentException(
                $"At most {IHostsService.MaxSummaryBatchSize} hosts per summary request.", nameof(hostIds));

        if (requested.Count == 0) return [];

        await using var dbContext = DalService.GetContext();

        // Visibility first, through the scoped Hosts set: a host outside the caller's entities is
        // reported as absent rather than as a host with no findings.
        var visible = (await dbContext.Hosts
                .AsNoTracking()
                .Where(h => requested.Contains(h.Id))
                .Select(h => h.Id)
                .ToListAsync())
            .ToHashSet();

        if (visible.Count == 0) return [];

        // One grouped query for every host asked about. Grouping on the raw status and severity
        // keeps the SQL a plain GROUP BY; the closed-set test and the severity parse run over the
        // handful of groups that come back rather than over the findings themselves.
        var visibleIds = visible.ToList();
        var groups = await dbContext.Vulnerabilities
            .AsNoTracking()
            .Where(v => v.HostId != null && visibleIds.Contains(v.HostId.Value))
            .GroupBy(v => new { HostId = v.HostId!.Value, v.Status, v.Severity })
            .Select(g => new { g.Key.HostId, g.Key.Status, g.Key.Severity, Count = g.Count() })
            .ToListAsync();

        var summaries = visibleIds.ToDictionary(id => id, id => new HostVulnerabilitySummaryDto { HostId = id });

        foreach (var group in groups)
        {
            var summary = summaries[group.HostId];
            summary.Total += group.Count;

            if (ClosedStatuses.IsClosed(group.Status)) continue;

            switch (ParseSeverity(group.Severity))
            {
                case NormalizedSeverity.Critical: summary.Open.Critical += group.Count; break;
                case NormalizedSeverity.High: summary.Open.High += group.Count; break;
                case NormalizedSeverity.Medium: summary.Open.Medium += group.Count; break;
                case NormalizedSeverity.Low: summary.Open.Low += group.Count; break;
                default: summary.Open.None += group.Count; break;
            }
        }

        return requested.Where(visible.Contains).Select(id => summaries[id]).ToList();
    }

    /// <summary>
    /// <c>vulnerabilities.severity</c> is the importers' <see cref="NormalizedSeverity"/> written as
    /// its number, "0" to "4". Anything else — blank, a word, an out-of-range number — is None, so a
    /// malformed row is still counted somewhere instead of disappearing from the total.
    /// </summary>
    public static NormalizedSeverity ParseSeverity(string? severity) =>
        int.TryParse(severity?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        && value is >= (int)NormalizedSeverity.None and <= (int)NormalizedSeverity.Critical
            ? (NormalizedSeverity)value
            : NormalizedSeverity.None;
}
