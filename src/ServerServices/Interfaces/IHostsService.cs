using System.Linq.Expressions;
using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IHostsService
{

    /// <summary>
    /// Verify if hosts exists 
    /// </summary>
    /// <param name="hostIp"></param>
    /// <returns></returns>
    public Task<bool> HostExistsAsync(string hostIp);
    
    /// <summary>
    ///   Get all hosts
    /// </summary>
    /// <returns></returns>
    public List<Host> GetAll();

    /// <summary>
    ///  Get host by id
    /// </summary>
    /// <param name="hostId"></param>
    /// <returns></returns>
    public Host GetById(int hostId);
    
    /// <summary>
    /// Get host by ip
    /// </summary>
    /// <param name="hostIp"></param>
    /// <returns></returns>
    public Host GetByIp(string hostIp);

    public Task<Host> GetByIpAsync(string hostIp);
    
    /// <summary>
    /// Delete host by id
    /// </summary>
    /// <param name="hostId"></param>
    public void Delete(int hostId);
    
    /// <summary>
    /// Create new host
    /// </summary>
    /// <param name="host"></param>
    /// <returns></returns>
    public Host Create(Host host);
    public Task<Host> CreateAsync(Host host);
    
    /// <summary>
    /// Update a host
    /// </summary>
    /// <param name="host"></param>
    public void Update(Host host);
    
    public Task UpdateAsync(Host host);
    
    /// <summary>
    /// Get host services
    /// </summary>
    /// <param name="hostId"></param>
    /// <returns></returns>
    public List<HostsService> GetHostServices(int hostId);
    
    /// <summary>
    /// Gets a host service
    /// </summary>
    /// <param name="hostId"></param>
    /// <param name="serviceId"></param>
    /// <returns></returns>
    public HostsService GetHostService(int hostId, int serviceId);
    
    /// <summary>
    /// Check if host has the specified service
    /// </summary>
    /// <param name="hostId"></param>
    /// <param name="name"></param>
    /// <param name="port"></param>
    /// <param name="protocol"></param>
    /// <returns></returns>
    public bool HostHasService(int hostId, string name, int? port, string protocol);
    public Task<bool> HostHasServiceAsync(int hostId, string name, int? port, string protocol);
    
    /// <summary>
    /// Create and add a service to a host
    /// </summary>
    /// <param name="hostId"></param>
    /// <param name="service"></param>
    /// <returns></returns>
    public DAL.Entities.HostsService CreateAndAddService(int hostId, DAL.Entities.HostsService service);
    public Task<DAL.Entities.HostsService> CreateAndAddServiceAsync(int hostId, DAL.Entities.HostsService service);
    
    /// <summary>
    /// Delete a service from a host
    /// </summary>
    /// <param name="hostId"></param>
    /// <param name="serviceId"></param>
    public void DeleteService(int hostId, int serviceId);
    
    /// <summary>
    /// Update a service from a host
    /// </summary>
    /// <param name="hostId"></param>
    /// <param name="service"></param>
    public void UpdateService(int hostId, HostsService service);

    /// <summary>
    /// Finds a service using a linq expression
    /// </summary>
    /// <param name="hostId"></param>
    /// <param name="expression"></param>
    /// <returns></returns>
    public HostsService FindService(int hostId, Expression<Func<HostsService,bool>> expression);
    public Task<HostsService> FindServiceAsync(int hostId, Expression<Func<HostsService,bool>> expression);
    
    /// <summary>
    /// Get vulnerabilities from a host
    /// </summary>
    /// <param name="hostId"></param>
    public List<Vulnerability> GetVulnerabilities (int hostId);

}