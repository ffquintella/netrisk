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
}