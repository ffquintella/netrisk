using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IIncidentsService
{
    
    /// <summary>
    /// Get all incidents
    /// </summary>
    /// <returns></returns>
    public Task<List<Incident>> GetAllAsync();
    
    
    /// <summary>
    /// Get an incident by its id
    /// </summary>
    /// <returns></returns>
    public Task<Incident> GetByIdAsync(int id);
    
    /// <summary>
    /// Create a new incident
    /// </summary>
    /// <param name="incident"></param>
    /// <param name="user"></param>
    /// <returns></returns>
    public Task<Incident> CreateAsync(Incident incident, User user);
    
    /// <summary>
    /// Update an existing incident
    /// </summary>
    /// <param name="incident"></param>
    /// <param name="user"></param>
    /// <returns></returns>
    public Task UpdateAsync(Incident incident, User user);

    
    /// <summary>
    /// Delete an incident
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public Task DeleteByIdAsync(int id);
}