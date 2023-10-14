using DAL.Entities;

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
    public Host? Create(Host host);


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
}