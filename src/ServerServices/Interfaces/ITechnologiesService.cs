using DAL.Entities;

namespace ServerServices.Interfaces;

public interface ITechnologiesService
{
    
    /// <summary>
    /// List All thechnologies
    /// </summary>
    /// <returns></returns>
    public List<Technology> GetAll();

    /// <summary>
    /// List all technologies async
    /// </summary>
    /// <returns></returns>
    public Task<List<Technology>> GetAllAsync();
    
    /// <summary>
    /// Adds a new technology
    /// </summary>
    /// <param name="name"></param>
    public Task AddTechnologyAsync(string name);
    
    /// <summary>
    /// Removes an existing technology
    /// </summary>
    /// <param name="name"></param>
    public Task RemoveTechnologyAsync(string name);
}