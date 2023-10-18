using DAL.Entities;
using Model.DTO;

namespace ClientServices.Interfaces;

public interface IHostsService
{
    /// <summary>
    /// Get one host
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public Host? GetOne(int id);
    
    /// <summary>
    /// Get all hosts
    /// </summary>
    /// <returns></returns>
    public List<Host> GetAll();
    
    /// <summary>
    /// Create a new host
    /// </summary>
    /// <param name="host"></param>
    /// <returns></returns>
    public Task<Host?> Create(Host host);


    /// <summary>
    /// Update a host
    /// </summary>
    /// <param name="host"></param>
    public void Update(Host host);
    
    /// <summary>
    /// Check if the host exists
    /// </summary>
    /// <param name="hostIp"></param>
    /// <returns></returns>
    public bool HostExists(string hostIp);
    
    
    /// <summary>
    /// Get host by ip
    /// </summary>
    /// <param name="hostIp"></param>
    /// <returns></returns>
    public Host? GetByIp(string hostIp);
    
    /// <summary>
    /// Gets a host service
    /// </summary>
    /// <param name="hostId"></param>
    /// <param name="serviceId"></param>
    /// <returns></returns>
    public HostsService GetHostService(int hostId, int serviceId);
    
    /// <summary>
    /// Get all host services
    /// </summary>
    /// <param name="hostId"></param>
    /// <returns></returns>
    public List<HostsService> GetAllHostService(int hostId);
    
    
    /// <summary>
    /// Get all host vulnerabilities
    /// </summary>
    /// <param name="hostId"></param>
    /// <returns></returns>
    public List<Vulnerability> GetAllHostVulnerabilities(int hostId);
    
    /// <summary>
    /// Check if host has the specified service
    /// </summary>
    /// <param name="hostId"></param>
    /// <param name="name"></param>
    /// <param name="port"></param>
    /// <param name="protocol"></param>
    /// <returns></returns>
    public Task<bool> HostHasService(int hostId, string name, int? port, string protocol);
    
    /// <summary>
    /// Create and add a service to a host
    /// </summary>
    /// <param name="hostId"></param>
    /// <param name="service"></param>
    /// <returns></returns>
    public Task<HostsService> CreateAndAddService(int hostId, HostsServiceDto service);
    
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
    public void UpdateService(int hostId, HostsServiceDto service);
    
    public Task<HostsService> FindService(int hostId, string name, int? port, string protocol);
}