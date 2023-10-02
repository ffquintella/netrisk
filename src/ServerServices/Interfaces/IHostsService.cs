using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IHostsService
{
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
    
    /// <summary>
    /// Update a host
    /// </summary>
    /// <param name="host"></param>
    public void Update(Host host);
    
}