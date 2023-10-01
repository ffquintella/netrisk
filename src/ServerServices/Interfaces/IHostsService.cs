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
}